using HireLens.Api.Domain;

namespace HireLens.Tests.Domain;

public class JobApplicationStatusTests
{
    private static readonly DateTime AppliedAt = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = AppliedAt.AddHours(2);

    private static JobApplication NewApplication() =>
        JobApplication.Create("job-1", "Alice", "alice@example.com", "resume", AppliedAt);

    [Fact]
    public void New_application_starts_applied_with_one_history_entry()
    {
        var app = NewApplication();

        Assert.Equal(ApplicationStatus.Applied, app.Status);
        var entry = Assert.Single(app.StatusHistory);
        Assert.Equal(new StatusChange(ApplicationStatus.Applied, AppliedAt), entry);
    }

    [Theory]
    [InlineData(ApplicationStatus.Shortlisted)]
    [InlineData(ApplicationStatus.Rejected)]
    public void Shortlist_or_reject_changes_status_and_appends_history(ApplicationStatus target)
    {
        var app = NewApplication();

        var outcome = app.ChangeStatus(target, Later);

        Assert.Equal(StatusChangeOutcome.Changed, outcome);
        Assert.Equal(target, app.Status);
        Assert.Equal(2, app.StatusHistory.Count);
        Assert.Equal(new StatusChange(target, Later), app.StatusHistory[^1]);
    }

    [Fact]
    public void Recruiter_can_change_their_mind_between_shortlisted_and_rejected()
    {
        var app = NewApplication();

        app.ChangeStatus(ApplicationStatus.Shortlisted, Later);
        var outcome = app.ChangeStatus(ApplicationStatus.Rejected, Later.AddMinutes(5));

        Assert.Equal(StatusChangeOutcome.Changed, outcome);
        Assert.Equal(ApplicationStatus.Rejected, app.Status);
        Assert.Equal(
            [ApplicationStatus.Applied, ApplicationStatus.Shortlisted, ApplicationStatus.Rejected],
            app.StatusHistory.Select(h => h.Status));
    }

    [Fact]
    public void Setting_the_same_status_again_is_a_no_op()
    {
        var app = NewApplication();
        app.ChangeStatus(ApplicationStatus.Shortlisted, Later);

        var outcome = app.ChangeStatus(ApplicationStatus.Shortlisted, Later.AddMinutes(1));

        Assert.Equal(StatusChangeOutcome.Unchanged, outcome);
        Assert.Equal(2, app.StatusHistory.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ApplicationStatus.Shortlisted)]
    [InlineData(ApplicationStatus.Rejected)]
    public void Cannot_move_back_to_applied(ApplicationStatus? startFrom)
    {
        var app = NewApplication();
        if (startFrom is { } s) app.ChangeStatus(s, Later);
        var before = app.StatusHistory.Count;
        var statusBefore = app.Status;

        var outcome = app.ChangeStatus(ApplicationStatus.Applied, Later.AddMinutes(1));

        Assert.Equal(StatusChangeOutcome.Invalid, outcome);
        Assert.Equal(statusBefore, app.Status);
        Assert.Equal(before, app.StatusHistory.Count);
    }
}
