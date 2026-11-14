using HireLens.Api.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HireLens.Api.Data;

public interface IJobRepository
{
    Task InsertAsync(Job job, CancellationToken ct);
    Task<Job?> GetAsync(string id, CancellationToken ct);
    /// <param name="status">Filter by status, or null for all jobs.</param>
    Task<List<Job>> ListAsync(JobStatus? status, CancellationToken ct);
    Task<bool> ReplaceAsync(Job job, CancellationToken ct);
}

public sealed class JobRepository(IMongoDatabase db) : IJobRepository
{
    public const string CollectionName = "jobs";
    private readonly IMongoCollection<Job> _jobs = db.GetCollection<Job>(CollectionName);

    public Task InsertAsync(Job job, CancellationToken ct) =>
        _jobs.InsertOneAsync(job, cancellationToken: ct);

    public async Task<Job?> GetAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _jobs.Find(j => j.Id == id).FirstOrDefaultAsync(ct);
    }

    public Task<List<Job>> ListAsync(JobStatus? status, CancellationToken ct)
    {
        var filter = status is { } s
            ? Builders<Job>.Filter.Eq(j => j.Status, s)
            : Builders<Job>.Filter.Empty;

        return _jobs.Find(filter).SortByDescending(j => j.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> ReplaceAsync(Job job, CancellationToken ct)
    {
        var result = await _jobs.ReplaceOneAsync(j => j.Id == job.Id, job, cancellationToken: ct);
        return result.MatchedCount == 1;
    }
}
