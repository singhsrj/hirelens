using System.Text.Json;
using HireLens.Api.Data;
using HireLens.Api.Domain;
using HireLens.Api.Ner;
using MongoDB.Driver;

namespace HireLens.Api.Seed;

/// <summary>
/// Loads the demo jobs and applicants from demo-data.json. Run with:
///   dotnet run --project src/HireLens.Api -- seed            (only if the database has no jobs)
///   dotnet run --project src/HireLens.Api -- seed --reset    (wipes jobs and applications first)
/// Skills are extracted by the real NER service; if it's unreachable, applicants are saved as pending,
/// exactly as a live application would be.
/// </summary>
public sealed class DemoSeeder(
    IMongoDatabase db,
    IJobRepository jobs,
    IApplicationRepository applications,
    INerClient ner,
    TimeProvider time,
    ILogger<DemoSeeder> logger)
{
    private sealed record SeedFile(List<SeedJob> Jobs, List<SeedApplicant> Applicants);
    private sealed record SeedJob(string Key, string Title, string Location, string DescriptionText,
        List<string> MustHaveSkills, List<string> NiceToHaveSkills, int DaysAgo);
    private sealed record SeedApplicant(string Job, string Name, string Email, string ResumeText, int DaysAgo, string? Status);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<int> RunAsync(bool reset, CancellationToken ct)
    {
        var jobsCollection = db.GetCollection<Job>(JobRepository.CollectionName);
        if (reset)
        {
            await jobsCollection.DeleteManyAsync(FilterDefinition<Job>.Empty, ct);
            await db.GetCollection<JobApplication>(ApplicationRepository.CollectionName)
                .DeleteManyAsync(FilterDefinition<JobApplication>.Empty, ct);
            logger.LogInformation("Removed existing jobs and applications");
        }
        else if (await jobsCollection.CountDocumentsAsync(FilterDefinition<Job>.Empty, cancellationToken: ct) > 0)
        {
            logger.LogWarning("The database already has jobs; nothing seeded. Use 'seed --reset' to replace them");
            return 1;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "demo-data.json");
        var data = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path, ct), Json)
                   ?? throw new InvalidOperationException($"Could not read {path}");

        var now = time.GetUtcNow().UtcDateTime;
        var jobsByKey = new Dictionary<string, Job>();
        foreach (var j in data.Jobs)
        {
            var job = new Job
            {
                Title = j.Title,
                Location = j.Location,
                DescriptionText = j.DescriptionText,
                MustHaveSkills = SkillList.Normalize(j.MustHaveSkills),
                NiceToHaveSkills = SkillList.Normalize(j.NiceToHaveSkills),
                Status = JobStatus.Open,
                CreatedAt = now.AddDays(-j.DaysAgo),
            };
            await jobs.InsertAsync(job, ct);
            jobsByKey[j.Key] = job;
        }

        int pending = 0;
        foreach (var a in data.Applicants)
        {
            var job = jobsByKey[a.Job];
            var appliedAt = now.AddDays(-a.DaysAgo).AddMinutes(-Random.Shared.Next(0, 600));
            var application = JobApplication.Create(job.Id!, a.Name, a.Email.ToLowerInvariant(), a.ResumeText, appliedAt);

            if (await ner.ExtractAsync(a.ResumeText, ct) is { } extraction)
                application.RecordExtraction(extraction.Skills,
                    new ExtractedEntities { Orgs = extraction.Orgs, Locations = extraction.Locations, Dates = extraction.Dates });
            else
                pending++;

            application.ApplyMatch(ScoreCalculator.Calculate(job.MustHaveSkills, application.Skills));

            if (a.Status is { } status)
                application.ChangeStatus(Enum.Parse<ApplicationStatus>(status, ignoreCase: true), appliedAt.AddHours(20));

            await applications.TryInsertAsync(application, ct);
        }

        logger.LogInformation("Seeded {Jobs} jobs and {Applicants} applicants ({Pending} pending NER)",
            data.Jobs.Count, data.Applicants.Count, pending);
        if (pending > 0)
            logger.LogWarning("The NER service was unreachable for {Pending} applicants. Start it and use 'Read skills for all' on the applicants page", pending);
        return 0;
    }
}
