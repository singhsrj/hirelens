using HireLens.Api.Data;
using HireLens.Api.Domain;
using MongoDB.Bson;

namespace HireLens.Tests.Fakes;

// Minimal in-memory stand-ins for the Mongo repositories, for service-level tests.
// Like a real database they store and hand out *copies*, so a service that mutates an object
// but forgets to persist it will fail the tests instead of passing by accident.

public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly Dictionary<string, Job> _jobs = [];

    public Job Stored(string id) => Copy(_jobs[id]);

    public Task InsertAsync(Job job, CancellationToken ct)
    {
        job.Id ??= ObjectId.GenerateNewId().ToString();
        _jobs[job.Id] = Copy(job);
        return Task.CompletedTask;
    }

    public Task<Job?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(_jobs.TryGetValue(id, out var j) ? Copy(j) : null);

    public Task<List<Job>> ListAsync(JobStatus? status, CancellationToken ct) =>
        Task.FromResult(_jobs.Values.Where(j => status is null || j.Status == status)
            .OrderByDescending(j => j.CreatedAt).Select(Copy).ToList());

    public Task<bool> ReplaceAsync(Job job, CancellationToken ct)
    {
        if (job.Id is null || !_jobs.ContainsKey(job.Id)) return Task.FromResult(false);
        _jobs[job.Id] = Copy(job);
        return Task.FromResult(true);
    }

    private static Job Copy(Job j) => new()
    {
        Id = j.Id, Title = j.Title, Location = j.Location, DescriptionText = j.DescriptionText,
        MustHaveSkills = [.. j.MustHaveSkills], NiceToHaveSkills = [.. j.NiceToHaveSkills],
        Status = j.Status, CreatedAt = j.CreatedAt,
    };
}

public sealed class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly Dictionary<string, JobApplication> _apps = [];

    public JobApplication Stored(string id) => Copy(_apps[id]);
    public IEnumerable<JobApplication> All => _apps.Values.Select(Copy);

    public Task<bool> TryInsertAsync(JobApplication application, CancellationToken ct)
    {
        if (_apps.Values.Any(a => a.JobId == application.JobId && a.Email == application.Email))
            return Task.FromResult(false);

        application.Id ??= ObjectId.GenerateNewId().ToString();
        _apps[application.Id] = Copy(application);
        return Task.FromResult(true);
    }

    public Task<JobApplication?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(_apps.TryGetValue(id, out var a) ? Copy(a) : null);

    public Task<List<JobApplication>> ListRankedAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(_apps.Values.Where(a => a.JobId == jobId)
            .OrderByDescending(a => a.Score).ThenBy(a => a.CreatedAt).Select(Copy).ToList());

    public Task<List<JobApplication>> ListForRescoringAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(_apps.Values.Where(a => a.JobId == jobId).Select(Copy).ToList());

    public Task UpdateScoresAsync(IEnumerable<(string Id, MatchResult Match)> updates, CancellationToken ct)
    {
        foreach (var (id, match) in updates)
            _apps[id].ApplyMatch(match);
        return Task.CompletedTask;
    }

    public Task<bool> AppendStatusAsync(string id, StatusChange change, CancellationToken ct)
    {
        if (!_apps.TryGetValue(id, out var stored)) return Task.FromResult(false);
        stored.Status = change.Status;      // $set
        stored.StatusHistory.Add(change);   // $push
        return Task.FromResult(true);
    }

    public Task<bool> SaveExtractionAsync(JobApplication a, CancellationToken ct)
    {
        if (!_apps.TryGetValue(a.Id!, out var stored) || stored.NerStatus != NerStatus.Pending)
            return Task.FromResult(false);
        stored.NerStatus = a.NerStatus;
        stored.Skills = [.. a.Skills];
        stored.Entities = a.Entities;
        stored.ApplyMatch(new MatchResult(a.Score, a.Matched, a.Missing));
        return Task.FromResult(true);
    }

    private static JobApplication Copy(JobApplication a) => new()
    {
        Id = a.Id, JobId = a.JobId, Name = a.Name, Email = a.Email, ResumeText = a.ResumeText,
        NerStatus = a.NerStatus, Skills = [.. a.Skills],
        Entities = new ExtractedEntities { Orgs = [.. a.Entities.Orgs], Locations = [.. a.Entities.Locations], Dates = [.. a.Entities.Dates] },
        Score = a.Score, Matched = [.. a.Matched], Missing = [.. a.Missing],
        Status = a.Status, StatusHistory = [.. a.StatusHistory], CreatedAt = a.CreatedAt,
    };
}
