using System.ComponentModel.DataAnnotations;
using HireLens.Api.Domain;

namespace HireLens.Api.Features.Applications;

/// <summary>A candidate's application. Skills are extracted from the resume text by the NER service.</summary>
public sealed record ApplyRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(50_000, MinimumLength = 50)] string ResumeText);

public sealed record ChangeStatusRequest([Required] ApplicationStatus? Status);

/// <summary>One row in the recruiter's ranked list (no resume text).</summary>
public sealed record ApplicantSummary(
    string Id,
    string JobId,
    string Name,
    string Email,
    NerStatus NerStatus,
    List<string> Skills,
    double Score,
    List<string> Matched,
    List<string> Missing,
    ApplicationStatus Status,
    DateTime CreatedAt)
{
    public static ApplicantSummary From(JobApplication a) => new(
        a.Id!, a.JobId, a.Name, a.Email, a.NerStatus, a.Skills, a.Score, a.Matched, a.Missing, a.Status, a.CreatedAt);
}

public sealed record ApplicationDetail(
    string Id,
    string JobId,
    string Name,
    string Email,
    string ResumeText,
    NerStatus NerStatus,
    List<string> Skills,
    ExtractedEntities Entities,
    double Score,
    List<string> Matched,
    List<string> Missing,
    ApplicationStatus Status,
    List<StatusChange> StatusHistory,
    DateTime CreatedAt)
{
    public static ApplicationDetail From(JobApplication a) => new(
        a.Id!, a.JobId, a.Name, a.Email, a.ResumeText, a.NerStatus, a.Skills, a.Entities,
        a.Score, a.Matched, a.Missing, a.Status, a.StatusHistory, a.CreatedAt);
}
