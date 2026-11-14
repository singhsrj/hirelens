using HireLens.Api.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HireLens.Api.Features.Applications;

public static class ApplicationEndpoints
{
    public const string ApplyRateLimitPolicy = "apply";

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var byJob = app.MapGroup("/api/jobs/{jobId}/applications").WithTags("Applications");

        byJob.MapPost("/", Apply)
            .RequireRateLimiting(ApplyRateLimitPolicy)
            .WithSummary("Candidate applies to a job");
        byJob.MapGet("/", ListRanked)
            .RequireAuthorization(AuthEndpoints.RecruiterPolicy)
            .WithSummary("Applicants for a job, ranked by score");

        // Everything about existing applications is recruiter-only: it holds candidates' personal data.
        var applications = app.MapGroup("/api/applications")
            .WithTags("Applications")
            .RequireAuthorization(AuthEndpoints.RecruiterPolicy);

        applications.MapGet("/{id}", Get).WithSummary("Full application, including resume text");
        applications.MapPatch("/{id}/status", ChangeStatus).WithSummary("Shortlist or reject an applicant");
        applications.MapPost("/{id}/retry-ner", RetryNer).WithSummary("Re-run skill extraction on a pending application");

        return app;
    }

    private static async Task<Results<Created<ApplicationDetail>, ProblemHttpResult>> Apply(
        string jobId, ApplyRequest request, ApplicationService service, CancellationToken ct)
    {
        var result = await service.ApplyAsync(jobId, request, ct);
        if (result.Error is { } error) return error.ToProblem();

        var app = result.Value!;
        return TypedResults.Created($"/api/applications/{app.Id}", ApplicationDetail.From(app));
    }

    private static async Task<Results<Ok<List<ApplicantSummary>>, ProblemHttpResult>> ListRanked(
        string jobId, ApplicationService service, CancellationToken ct)
    {
        var result = await service.ListRankedAsync(jobId, ct);
        return result.Error is { } error
            ? error.ToProblem()
            : TypedResults.Ok(result.Value!.Select(ApplicantSummary.From).ToList());
    }

    private static async Task<Results<Ok<ApplicationDetail>, ProblemHttpResult>> Get(
        string id, ApplicationService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(ApplicationDetail.From(result.Value!));
    }

    private static async Task<Results<Ok<ApplicationDetail>, ProblemHttpResult>> RetryNer(
        string id, ApplicationService service, CancellationToken ct)
    {
        var result = await service.RetryNerAsync(id, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(ApplicationDetail.From(result.Value!));
    }

    private static async Task<Results<Ok<ApplicationDetail>, ProblemHttpResult>> ChangeStatus(
        string id, ChangeStatusRequest request, ApplicationService service, CancellationToken ct)
    {
        var result = await service.ChangeStatusAsync(id, request.Status!.Value, ct);
        return result.Error is { } error ? error.ToProblem() : TypedResults.Ok(ApplicationDetail.From(result.Value!));
    }
}
