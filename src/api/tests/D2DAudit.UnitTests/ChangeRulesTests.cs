using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class ChangeRulesTests
{
    [Fact]
    public void A_deployed_change_without_approval_fails()
    {
        var deployed = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var result = ChangeRules.Evaluate(
            [
                ("CHG-01", deployed.AddDays(-1), deployed),
                ("CHG-02", null, deployed),
                ("CHG-03", null, null),
            ]);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("CHG-02", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CHG-01", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CHG-03", result.Detail, StringComparison.Ordinal);
        Assert.Equal("production changes are reviewed", ChangeRules.TestName);
    }
}
