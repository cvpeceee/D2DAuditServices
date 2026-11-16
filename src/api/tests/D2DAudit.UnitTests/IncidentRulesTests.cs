using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class IncidentRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void A_late_resolution_fails_the_sla_test()
    {
        var reported = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var resolved = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var result = IncidentRules.Evaluate(
            [
                ("INC-01", "Resolved", new DateOnly(2026, 11, 3), reported.AddDays(1)),
                ("INC-03", "Resolved", new DateOnly(2026, 10, 27), resolved),
            ],
            Today);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("INC-03", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("INC-01", result.Detail, StringComparison.Ordinal);
        Assert.Equal(1, IncidentRules.Days("Critical"));
        Assert.Equal(14, IncidentRules.Days("Low"));
    }
}
