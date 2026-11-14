using HireLens.Api.Common;
using HireLens.Api.Data;
using HireLens.Api.Domain;

namespace HireLens.Api.Features.Applications;

public sealed class ApplicationService(IJobRepository jobs, IApplicationRepository applications, TimeProvider time)
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

        // Phase 2: skills are entered by hand. NER (Phase 4) will fill these and flip NerStatus to Done.
        application.Skills = SkillList.Normalize(request.Skills);
        application.ApplyMatch(ScoreCalculator.Calculate(job.MustHaveSkills, application.Skills));

        if (!await applications.TryInsertAsync(application, ct))
            return Error.Conflict("An application with this email already exists for this job.");

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
}
