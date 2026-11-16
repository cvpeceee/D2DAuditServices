using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class Milestone6RulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void A_new_hire_needs_a_clear_check_or_an_exclusion_with_a_reason()
    {
        var hired = Today.AddDays(-12);
        var people = new[]
        {
            (PersonState.Active, (DateOnly?)hired, "Pending", false, "", "Riley Chen"),
            (PersonState.Active, (DateOnly?)Today.AddDays(-40), "Clear", false, "", "Avery Dunn"),
            (PersonState.Active, (DateOnly?)Today.AddDays(-20), "Excluded", true, "Agency screening is on file.", "Casey Ruiz"),
            (PersonState.Active, (DateOnly?)Today.AddDays(-200), "NotStarted", false, "", "Sam Tenured"),
            (PersonState.Offboarded, (DateOnly?)Today.AddDays(-5), "NotStarted", false, "", "Sam Leaved"),
        };

        var result = BackgroundRules.Evaluate(people, Today);

        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("Riley Chen", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam Tenured", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam Leaved", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exclusion_without_a_reason_does_not_satisfy_the_check()
    {
        var people = new[]
        {
            (PersonState.Active, (DateOnly?)Today.AddDays(-8), "Excluded", true, "  ", "No Reason"),
        };

        var result = BackgroundRules.Evaluate(people, Today);

        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.False(BackgroundRules.Satisfied("Excluded", true, " "));
        Assert.True(BackgroundRules.Satisfied("Clear", false, ""));
    }

    [Fact]
    public void Evidence_outside_the_audit_window_is_excluded()
    {
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 9, 30);
        Assert.True(AuditWindow.Contains(start, end, new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(AuditWindow.Contains(start, end, new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(AuditWindow.Contains(start, end, null));
    }

    [Fact]
    public void Sla_due_dates_follow_the_severity_window()
    {
        var seen = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 8), VulnerabilityCalendar.Due(seen, "Critical", 7, 30, 60, 90));
        Assert.Equal(new DateOnly(2026, 12, 1), VulnerabilityCalendar.Due(seen, "High", 7, 30, 60, 90));
        Assert.Equal(60, VulnerabilityCalendar.Days("Unknown", 7, 30, 60, 90));
    }

    [Fact]
    public void Control_csv_keeps_quoted_commas_and_rejects_a_missing_column()
    {
        var parsed = ControlCsv.Parse("code,title,description,frequency,framework,requirement,coverage\nHL-02,\"Export, bulk\",Owner approves,Annual,iso27001,A.5.10,partial\n");
        Assert.True(parsed.IsSuccess);
        var row = Assert.Single(parsed.Value!);
        Assert.Equal("Export, bulk", row.Title);
        Assert.Equal("partial", row.Coverage);
        Assert.False(ControlCsv.Parse("code,title\nX,Y").IsSuccess);
    }
}
