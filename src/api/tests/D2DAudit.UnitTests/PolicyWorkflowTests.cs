using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class PolicyWorkflowTests
{
    [Fact]
    public void Submit_accepts_a_draft_only()
    {
        Assert.True(PolicyWorkflow.Submit(PolicyState.Draft).Succeeded);
        Assert.False(PolicyWorkflow.Submit(PolicyState.Approved).Succeeded);
    }

    [Fact]
    public void Approve_accepts_a_review_only()
    {
        Assert.True(PolicyWorkflow.Approve(PolicyState.InReview).Succeeded);
        Assert.Equal("not_in_review", PolicyWorkflow.Approve(PolicyState.Draft).Error);
    }

    [Fact]
    public void Reminder_is_due_once_a_day_after_the_due_date()
    {
        var today = new DateOnly(2026, 10, 21);
        Assert.False(AcknowledgementRules.NeedsReminder(today, null, null, today));
        Assert.False(AcknowledgementRules.NeedsReminder(today.AddDays(-1), new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), null, today));
        Assert.True(AcknowledgementRules.NeedsReminder(today.AddDays(-1), null, null, today));
        Assert.False(AcknowledgementRules.NeedsReminder(today.AddDays(-1), null, new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero), today));
        Assert.True(AcknowledgementRules.NeedsReminder(today.AddDays(-1), null, new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), today));
    }
}
