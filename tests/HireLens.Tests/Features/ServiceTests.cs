using HireLens.Api.Common;
using HireLens.Api.Domain;
using HireLens.Api.Features.Applications;
using HireLens.Api.Features.Jobs;
using HireLens.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace HireLens.Tests.Features;

public class ServiceTests
{
    private const string Resume = "Backend engineer with several years of C# and ASP.NET Core experience.";

    private readonly InMemoryJobRepository _jobs = new();
    private readonly InMemoryApplicationRepository _apps = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly JobService _jobService;
    private readonly ApplicationService _appService;

    public ServiceTests()
    {
        _jobService = new JobService(_jobs, _apps, _time);
        _appService = new ApplicationService(_jobs, _apps, _time);
    }

    private static JobRequest JobWith(List<string> mustHave, JobStatus? status = null) =>
        new("Software Engineer", "Remote", "Build APIs in C# backed by MongoDB.", mustHave, ["Python"], status);

    private static ApplyRequest Applicant(string email, List<string> skills) =>
        new("Candidate", email, Resume, skills);

    [Fact]
    public async Task Apply_saves_score_matched_and_missing()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#", "MongoDB"]), default);

        var result = await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", ["c#"]), default);

        Assert.Null(result.Error);
        var stored = _apps.Stored(result.Value!.Id!);
        Assert.Equal(0.5, stored.Score);
        Assert.Equal(["C#"], stored.Matched);
        Assert.Equal(["MongoDB"], stored.Missing);
        Assert.Equal(NerStatus.Pending, stored.NerStatus);
    }

    [Fact]
    public async Task Apply_normalises_email_so_case_variants_count_as_duplicates()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"]), default);
        await _appService.ApplyAsync(job.Id!, Applicant("Alice@X.com", []), default);

        var second = await _appService.ApplyAsync(job.Id!, Applicant("  alice@x.COM ", []), default);

        Assert.Equal(ErrorKind.Conflict, second.Error?.Kind);
    }

    [Fact]
    public async Task Apply_to_closed_job_is_a_conflict()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"], JobStatus.Closed), default);

        var result = await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", ["C#"]), default);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
        Assert.Empty(_apps.All);
    }

    [Fact]
    public async Task Apply_to_unknown_job_is_not_found()
    {
        var result = await _appService.ApplyAsync("aaaaaaaaaaaaaaaaaaaaaaaa", Applicant("a@x.com", []), default);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    [Fact]
    public async Task Ranked_list_orders_by_score_then_earliest_applicant()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#", "MongoDB"]), default);
        await _appService.ApplyAsync(job.Id!, Applicant("low@x.com", []), default);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _appService.ApplyAsync(job.Id!, Applicant("half-early@x.com", ["C#"]), default);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _appService.ApplyAsync(job.Id!, Applicant("top@x.com", ["C#", "MongoDB"]), default);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _appService.ApplyAsync(job.Id!, Applicant("half-late@x.com", ["MongoDB"]), default);

        var ranked = await _appService.ListRankedAsync(job.Id!, default);

        Assert.Equal(
            ["top@x.com", "half-early@x.com", "half-late@x.com", "low@x.com"],
            ranked.Value!.Select(a => a.Email));
    }

    [Fact]
    public async Task Changing_status_persists_status_and_history()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"]), default);
        var app = (await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", []), default)).Value!;
        _time.Advance(TimeSpan.FromHours(1));

        var result = await _appService.ChangeStatusAsync(app.Id!, ApplicationStatus.Shortlisted, default);

        Assert.Null(result.Error);
        var stored = _apps.Stored(app.Id!);
        Assert.Equal(ApplicationStatus.Shortlisted, stored.Status);
        Assert.Equal(
            [new StatusChange(ApplicationStatus.Applied, _time.GetUtcNow().UtcDateTime.AddHours(-1)),
             new StatusChange(ApplicationStatus.Shortlisted, _time.GetUtcNow().UtcDateTime)],
            stored.StatusHistory);
    }

    [Fact]
    public async Task Changing_status_back_to_applied_is_invalid_and_saves_nothing()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"]), default);
        var app = (await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", []), default)).Value!;

        var result = await _appService.ChangeStatusAsync(app.Id!, ApplicationStatus.Applied, default);

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
        Assert.Single(_apps.Stored(app.Id!).StatusHistory);
    }

    [Fact]
    public async Task Repeating_a_status_change_does_not_duplicate_history()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"]), default);
        var app = (await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", []), default)).Value!;

        await _appService.ChangeStatusAsync(app.Id!, ApplicationStatus.Rejected, default);
        await _appService.ChangeStatusAsync(app.Id!, ApplicationStatus.Rejected, default);

        Assert.Equal(2, _apps.Stored(app.Id!).StatusHistory.Count);
    }

    [Fact]
    public async Task Editing_must_have_skills_rescores_existing_applications()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#", "MongoDB", "JavaScript"]), default);
        var app = (await _appService.ApplyAsync(job.Id!, Applicant("a@x.com", ["C#"]), default)).Value!;
        Assert.Equal(0.3333, _apps.Stored(app.Id!).Score);

        var result = await _jobService.UpdateAsync(job.Id!, JobWith(["C#", "MongoDB"]), default);

        Assert.Null(result.Error);
        var stored = _apps.Stored(app.Id!);
        Assert.Equal(0.5, stored.Score);
        Assert.Equal(["MongoDB"], stored.Missing);
    }

    [Fact]
    public async Task Update_without_status_keeps_the_current_status()
    {
        var job = await _jobService.CreateAsync(JobWith(["C#"], JobStatus.Closed), default);

        await _jobService.UpdateAsync(job.Id!, JobWith(["C#"]), default);

        Assert.Equal(JobStatus.Closed, _jobs.Stored(job.Id!).Status);
    }
}
