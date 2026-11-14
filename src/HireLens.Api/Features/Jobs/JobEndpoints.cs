using HireLens.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HireLens.Api.Features.Jobs;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs").WithTags("Jobs");

        jobs.MapPost("/", CreateJob).WithSummary("Create a job");
        jobs.MapGet("/", ListJobs).WithSummary("List jobs (open by default)");
        jobs.MapGet("/{id}", GetJob).WithSummary("Job details");
        jobs.MapPut("/{id}", UpdateJob).WithSummary("Edit a job, or close/reopen it");

        return app;
    }

    private static async Task<Created<JobResponse>> CreateJob(JobRequest request, JobService service, CancellationToken ct)
    {
        var job = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/jobs/{job.Id}", JobResponse.From(job));
    }

    /// <param name="status">"open" (default), "closed" or "all".</param>
    private static async Task<Results<Ok<List<JobResponse>>, ProblemHttpResult>> ListJobs(
        JobService service, CancellationToken ct, string? status = "open")
    {
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

    private static async Task<Results<Ok<JobResponse>, ProblemHttpResult>> UpdateJob(
        string id, JobRequest request, JobService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(JobResponse.From(result.Value!));
    }
}
