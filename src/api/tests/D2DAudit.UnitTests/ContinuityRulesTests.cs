using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class ContinuityRulesTests
{
    [Fact]
    public void A_failed_exercise_fails_the_recovery_test()
    {
        var result = ContinuityRules.Evaluate(
            [
                ("Quarterly warehouse restore", "Pass"),
                ("Region failover drill", "Fail"),
            ]);
        var empty = ContinuityRules.Evaluate([]);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("Region failover drill", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Quarterly warehouse restore", result.Detail, StringComparison.Ordinal);
        Assert.Equal(TestOutcome.Fail, empty.Outcome);
        Assert.Equal("Recovery exercises are recorded", ContinuityRules.TestName);
    }
}
