using HireLens.Api.Domain;
using MongoDB.Driver;

namespace HireLens.Api.Data;

/// <summary>
/// Creates indexes when the app starts. createIndexes is idempotent, so this is safe on every boot.
/// It fails fast: if Mongo is unreachable the app won't start, which is clearer than failing on the first request.
/// </summary>
public sealed class MongoIndexInitializer(IMongoDatabase db, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var apps = db.GetCollection<JobApplication>(ApplicationRepository.CollectionName);
        var appKeys = Builders<JobApplication>.IndexKeys;

        await apps.Indexes.CreateManyAsync(
        [
            // Ranked applicant list: equality on jobId, then the sort (score desc, oldest first for ties),
            // so Mongo walks the index in order and never does an in-memory sort.
            new CreateIndexModel<JobApplication>(
                appKeys.Ascending(a => a.JobId).Descending(a => a.Score).Ascending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "jobId_score_createdAt" }),

            // Multikey index: "which candidates know X?"
            new CreateIndexModel<JobApplication>(
                appKeys.Ascending(a => a.Skills),
                new CreateIndexOptions { Name = "skills" }),

            // One application per email per job.
            new CreateIndexModel<JobApplication>(
                appKeys.Ascending(a => a.JobId).Ascending(a => a.Email),
                new CreateIndexOptions { Name = "jobId_email_unique", Unique = true }),
        ], ct);

        var jobs = db.GetCollection<Job>(JobRepository.CollectionName);
        await jobs.Indexes.CreateOneAsync(
            new CreateIndexModel<Job>(
                Builders<Job>.IndexKeys.Ascending(j => j.Status).Descending(j => j.CreatedAt),
                new CreateIndexOptions { Name = "status_createdAt" }),
            cancellationToken: ct);

        logger.LogInformation("MongoDB indexes ensured");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
