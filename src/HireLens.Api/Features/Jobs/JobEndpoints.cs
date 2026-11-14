using System.Security.Claims;
using HireLens.Api.Domain;
using HireLens.Api.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HireLens.Api.Features.Jobs;

public static class JobEndpoints
{
    public const string SuggestRateLimitPolicy = "suggest-skills";

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs").WithTags("Jobs");

        // Public: candidates browse open jobs and read job details.
        jobs.MapGet("/", ListJobs).WithSummary("List jobs (open by default; closed/all need a recruiter)");
        jobs.MapGet("/{id}", GetJob).WithSummary("Job details");

        // Recruiter only.
        jobs.MapPost("/", CreateJob).RequireAuthorization(AuthEndpoints.RecruiterPolicy).WithSummary("Create a job");
        jobs.MapPut("/{id}", UpdateJob).RequireAuthorization(AuthEndpoints.RecruiterPolicy).WithSummary("Edit a job, or close/reopen it");
        jobs.MapPost("/extract-skills", SuggestSkills)
            .RequireAuthorization(AuthEndpoints.RecruiterPolicy)
            .RequireRateLimiting(SuggestRateLimitPolicy)
            .WithSummary("Suggest must-have skills from a job description (calls the NER service)");

        return app;
    }

    private static async Task<Created<JobResponse>> CreateJob(JobRequest request, JobService service, CancellationToken ct)
    {
        var job = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/jobs/{job.Id}", JobResponse.From(job));
    }

    /// <param name="status">"open" (default), "closed" or "all".</param>
    private static async Task<Results<Ok<List<JobResponse>>, ProblemHttpResult>> ListJobs(
        JobService service, ClaimsPrincipal user, CancellationToken ct, string? status = "open")
    {
        if (status is not (null or "" or "open") && !user.IsRecruiter())
            return TypedResults.Problem(detail: "Sign in as a recruiter to see closed jobs.", statusCode: StatusCodes.Status401Unauthorized);

        JobStatus? filter;
        switch (status?.ToLowerInvariant())
        {
            case null or "" or "open": filter = JobStatus.Open; break;
            case "closed": filter = JobStatus.Closed; break;
            case "all": filter = null; break;
            default:
                return TypedResults.Problem(
                    detail: "status must be 'open', 'closed' or 'all'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var jobs = await service.ListAsync(filter, ct);
        return TypedResults.Ok(jobs.Select(JobResponse.From).ToList());
    }

    private static async Task<Results<Ok<JobResponse>, ProblemHttpResult>> GetJob(
        string id, JobService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(JobResponse.From(result.Value!));
    }

    private static async Task<Results<Ok<SuggestSkillsResponse>, ProblemHttpResult>> SuggestSkills(
        SuggestSkillsRequest request, JobService service, CancellationToken ct)
    {
        var result = await service.SuggestSkillsAsync(request.DescriptionText, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(new SuggestSkillsResponse(result.Value!));
    }

    private static async Task<Results<Ok<JobResponse>, ProblemHttpResult>> UpdateJob(
        string id, JobRequest request, JobService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(JobResponse.From(result.Value!));
    }
}
