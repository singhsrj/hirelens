using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HireLens.Api.Domain;

/// <summary>A candidate's application to one job. Stored whole as one document in the "applications" collection.</summary>
public sealed class JobApplication
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Reference to the job (not embedded: a job can have unlimited applications).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string JobId { get; set; } = "";

    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string ResumeText { get; set; } = "";

    public NerStatus NerStatus { get; set; } = NerStatus.Pending;
    public List<string> Skills { get; set; } = [];
    public ExtractedEntities Entities { get; set; } = new();

    public double Score { get; set; }
    public List<string> Matched { get; set; } = [];
    public List<string> Missing { get; set; } = [];

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;

    /// <summary>Embedded: small, bounded, and always read together with the application.</summary>
    public List<StatusChange> StatusHistory { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public static JobApplication Create(string jobId, string name, string email, string resumeText, DateTime now) => new()
    {
        JobId = jobId,
        Name = name,
        Email = email,
        ResumeText = resumeText,
        Status = ApplicationStatus.Applied,
        StatusHistory = [new StatusChange(ApplicationStatus.Applied, now)],
        CreatedAt = now,
    };

    /// <summary>Stores what NER found in the resume and marks extraction as done.</summary>
    public void RecordExtraction(IEnumerable<string> skills, ExtractedEntities entities)
    {
        Skills = SkillList.Normalize(skills);
        Entities = entities;
        NerStatus = NerStatus.Done;
    }

    public void ApplyMatch(MatchResult match)
    {
        Score = match.Score;
        Matched = [.. match.Matched];
        Missing = [.. match.Missing];
    }

    /// <summary>
    /// Moves the application to a recruiter decision. Shortlisted and Rejected can be switched between
    /// (recruiters change their minds), but nothing goes back to Applied. Setting the current status again
    /// is a no-op so retries are safe.
    /// </summary>
    public StatusChangeOutcome ChangeStatus(ApplicationStatus newStatus, DateTime at)
    {
        if (newStatus == ApplicationStatus.Applied)
            return StatusChangeOutcome.Invalid;

        if (newStatus == Status)
            return StatusChangeOutcome.Unchanged;

        Status = newStatus;
        StatusHistory.Add(new StatusChange(newStatus, at));
        return StatusChangeOutcome.Changed;
    }
}

public sealed record StatusChange(ApplicationStatus Status, DateTime At);

public enum StatusChangeOutcome { Changed, Unchanged, Invalid }

public sealed class ExtractedEntities
{
    public List<string> Orgs { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    public List<string> Dates { get; set; } = [];
}
