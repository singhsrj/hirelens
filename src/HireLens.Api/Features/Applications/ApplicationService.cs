using HireLens.Api.Common;
using HireLens.Api.Data;
using HireLens.Api.Domain;
using HireLens.Api.Ner;

namespace HireLens.Api.Features.Applications;

public sealed class ApplicationService(
    IJobRepository jobs,
    IApplicationRepository applications,
    INerClient ner,
    TimeProvider time,
    ILogger<ApplicationService> logger)
{
    public async Task<Result<JobApplication>> ApplyAsync(string jobId, ApplyRequest request, CancellationToken ct)
    {
        if (await jobs.GetAsync(jobId, ct) is not { } job)
            return Error.NotFound($"Job '{jobId}' was not found.");

        if (job.Status != JobStatus.Open)
            return Error.Conflict("This job is closed and no longer accepts applications.");

        var application = JobApplication.Create(
            jobId,
            request.Name.Trim(),
            request.Email.Trim().ToLowerInvariant(),
            request.ResumeText.Trim(),
            time.GetUtcNow().UtcDateTime);

        // If NER is down the application is still saved, with NerStatus = Pending and a score of 0,
        // and a recruiter can retry later. One failing service never blocks a candidate from applying.
        if (await ner.ExtractAsync(application.ResumeText, ct) is { } extraction)
            application.RecordExtraction(extraction.Skills, ToEntities(extraction));
        else
            logger.LogWarning("NER unavailable; saving application for job {JobId} as pending", jobId);

        application.ApplyMatch(ScoreCalculator.Calculate(job.MustHaveSkills, application.Skills));

        if (!await applications.TryInsertAsync(application, ct))
            return Error.Conflict("An application with this email already exists for this job.");

        return application;
    }

    /// <summary>Re-runs NER on an application that was saved while the NER service was down.</summary>
    public async Task<Result<JobApplication>> RetryNerAsync(string id, CancellationToken ct)
    {
        if (await applications.GetAsync(id, ct) is not { } application)
            return Error.NotFound($"Application '{id}' was not found.");

        if (application.NerStatus == NerStatus.Done)
            return Error.Conflict("Skills have already been extracted for this application.");

        if (await jobs.GetAsync(application.JobId, ct) is not { } job)
            return Error.NotFound($"Job '{application.JobId}' was not found.");

        if (await ner.ExtractAsync(application.ResumeText, ct) is not { } extraction)
            return Error.Unavailable("The skill extractor is unavailable right now. Try again in a minute.");

        application.RecordExtraction(extraction.Skills, ToEntities(extraction));
        application.ApplyMatch(ScoreCalculator.Calculate(job.MustHaveSkills, application.Skills));

        // False means a concurrent retry got there first; either way the stored result is equivalent.
        await applications.SaveExtractionAsync(application, ct);
        return application;
    }

    public async Task<Result<List<JobApplication>>> ListRankedAsync(string jobId, CancellationToken ct)
    {
        if (await jobs.GetAsync(jobId, ct) is null)
            return Error.NotFound($"Job '{jobId}' was not found.");

        return await applications.ListRankedAsync(jobId, ct);
    }

    public async Task<Result<JobApplication>> GetAsync(string id, CancellationToken ct) =>
        await applications.GetAsync(id, ct) is { } app ? app : Error.NotFound($"Application '{id}' was not found.");

    public async Task<Result<JobApplication>> ChangeStatusAsync(string id, ApplicationStatus status, CancellationToken ct)
    {
        if (await applications.GetAsync(id, ct) is not { } application)
            return Error.NotFound($"Application '{id}' was not found.");

        switch (application.ChangeStatus(status, time.GetUtcNow().UtcDateTime))
        {
            case StatusChangeOutcome.Invalid:
                return Error.Invalid("An application can only be shortlisted or rejected.");
            case StatusChangeOutcome.Unchanged:
                return application;
            default:
                if (!await applications.AppendStatusAsync(id, application.StatusHistory[^1], ct))
                    return Error.NotFound($"Application '{id}' was not found.");
                return application;
        }
    }

    private static ExtractedEntities ToEntities(NerResult r) =>
        new() { Orgs = r.Orgs, Locations = r.Locations, Dates = r.Dates };
}
