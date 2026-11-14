using HireLens.Api.Common;
using HireLens.Api.Data;
using HireLens.Api.Domain;
using HireLens.Api.Ner;

namespace HireLens.Api.Features.Jobs;

public sealed class JobService(IJobRepository jobs, IApplicationRepository applications, INerClient ner, TimeProvider time)
{
    public async Task<Job> CreateAsync(JobRequest request, CancellationToken ct)
    {
        var job = new Job { CreatedAt = time.GetUtcNow().UtcDateTime };
        Apply(job, request);
        job.Status = request.Status ?? JobStatus.Open;

        await jobs.InsertAsync(job, ct);
        return job;
    }

    public async Task<Result<Job>> GetAsync(string id, CancellationToken ct) =>
        await jobs.GetAsync(id, ct) is { } job ? job : Error.NotFound($"Job '{id}' was not found.");

    public Task<List<Job>> ListAsync(JobStatus? status, CancellationToken ct) => jobs.ListAsync(status, ct);

    /// <summary>
    /// Replaces the job's editable fields. If the must-have skills changed, every existing application
    /// is rescored so the ranked list never shows stale scores.
    /// </summary>
    public async Task<Result<Job>> UpdateAsync(string id, JobRequest request, CancellationToken ct)
    {
        if (await jobs.GetAsync(id, ct) is not { } job)
            return Error.NotFound($"Job '{id}' was not found.");

        var previousMustHave = job.MustHaveSkills;
        Apply(job, request);
        job.Status = request.Status ?? job.Status;

        if (!await jobs.ReplaceAsync(job, ct))
            return Error.NotFound($"Job '{id}' was not found.");

        if (!previousMustHave.SequenceEqual(job.MustHaveSkills))
            await RescoreApplicationsAsync(job, ct);

        return job;
    }

    /// <summary>Suggests skills from a job description. The recruiter confirms or edits them before saving.</summary>
    public async Task<Result<List<string>>> SuggestSkillsAsync(string descriptionText, CancellationToken ct) =>
        await ner.ExtractAsync(descriptionText, ct) is { } extraction
            ? SkillList.Normalize(extraction.Skills)
            : Error.Unavailable("Skill suggestions are unavailable right now. Add skills by hand, or try again in a minute.");

    private async Task RescoreApplicationsAsync(Job job, CancellationToken ct)
    {
        var existing = await applications.ListForRescoringAsync(job.Id!, ct);
        var updates = existing.Select(a => (a.Id!, ScoreCalculator.Calculate(job.MustHaveSkills, a.Skills)));
        await applications.UpdateScoresAsync(updates, ct);
    }

    private static void Apply(Job job, JobRequest r)
    {
        job.Title = r.Title.Trim();
        job.Location = r.Location?.Trim() ?? "";
        job.DescriptionText = r.DescriptionText.Trim();
        job.MustHaveSkills = SkillList.Normalize(r.MustHaveSkills);
        job.NiceToHaveSkills = SkillList.Normalize(r.NiceToHaveSkills);
    }
}
