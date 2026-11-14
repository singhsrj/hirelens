using HireLens.Api.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HireLens.Api.Data;

public interface IApplicationRepository
{
    /// <returns>False if this email has already applied to this job.</returns>
    Task<bool> TryInsertAsync(JobApplication application, CancellationToken ct);
    Task<JobApplication?> GetAsync(string id, CancellationToken ct);
    /// <summary>Applicants for a job, best score first. Resume text is left out to keep the list light.</summary>
    Task<List<JobApplication>> ListRankedAsync(string jobId, CancellationToken ct);
    /// <summary>Every application for a job, with just the fields needed to rescore it.</summary>
    Task<List<JobApplication>> ListForRescoringAsync(string jobId, CancellationToken ct);
    Task UpdateScoresAsync(IEnumerable<(string Id, MatchResult Match)> updates, CancellationToken ct);
    /// <summary>Sets the status and appends the matching history entry in one atomic update.</summary>
    Task<bool> AppendStatusAsync(string id, StatusChange change, CancellationToken ct);
    /// <summary>
    /// Saves NER output and the resulting score, but only if the application is still pending,
    /// so two overlapping retries can't both write. Returns false if nothing was updated.
    /// </summary>
    Task<bool> SaveExtractionAsync(JobApplication application, CancellationToken ct);
}

public sealed class ApplicationRepository(IMongoDatabase db) : IApplicationRepository
{
    public const string CollectionName = "applications";
    private readonly IMongoCollection<JobApplication> _apps = db.GetCollection<JobApplication>(CollectionName);
    private static readonly UpdateDefinitionBuilder<JobApplication> Update = Builders<JobApplication>.Update;
    private static readonly ProjectionDefinitionBuilder<JobApplication> Projection = Builders<JobApplication>.Projection;

    public async Task<bool> TryInsertAsync(JobApplication application, CancellationToken ct)
    {
        try
        {
            await _apps.InsertOneAsync(application, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<JobApplication?> GetAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _apps.Find(a => a.Id == id).FirstOrDefaultAsync(ct);
    }

    public Task<List<JobApplication>> ListRankedAsync(string jobId, CancellationToken ct) =>
        _apps.Find(a => a.JobId == jobId)
            .SortByDescending(a => a.Score)
            .ThenBy(a => a.CreatedAt)
            .Project<JobApplication>(Projection.Exclude(a => a.ResumeText))
            .ToListAsync(ct);

    public Task<List<JobApplication>> ListForRescoringAsync(string jobId, CancellationToken ct) =>
        _apps.Find(a => a.JobId == jobId)
            .Project<JobApplication>(Projection.Include(a => a.Id).Include(a => a.Skills))
            .ToListAsync(ct);

    public async Task UpdateScoresAsync(IEnumerable<(string Id, MatchResult Match)> updates, CancellationToken ct)
    {
        var writes = updates.Select(u => new UpdateOneModel<JobApplication>(
            Builders<JobApplication>.Filter.Eq(a => a.Id, u.Id),
            Update.Set(a => a.Score, u.Match.Score)
                  .Set(a => a.Matched, u.Match.Matched.ToList())
                  .Set(a => a.Missing, u.Match.Missing.ToList()))).ToList();

        if (writes.Count > 0)
            await _apps.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    public async Task<bool> AppendStatusAsync(string id, StatusChange change, CancellationToken ct)
    {
        var result = await _apps.UpdateOneAsync(
            a => a.Id == id,
            Update.Set(a => a.Status, change.Status).Push(a => a.StatusHistory, change),
            cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<bool> SaveExtractionAsync(JobApplication a, CancellationToken ct)
    {
        var result = await _apps.UpdateOneAsync(
            x => x.Id == a.Id && x.NerStatus == NerStatus.Pending,
            Update.Set(x => x.NerStatus, a.NerStatus)
                  .Set(x => x.Skills, a.Skills)
                  .Set(x => x.Entities, a.Entities)
                  .Set(x => x.Score, a.Score)
                  .Set(x => x.Matched, a.Matched)
                  .Set(x => x.Missing, a.Missing),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }
}
