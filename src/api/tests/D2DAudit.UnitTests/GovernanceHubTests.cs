using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class GovernanceHubTests
{
    [Fact]
    public void EntityRollupCalculator_computes_consolidated_headcount_and_readiness()
    {
        var entities = new LegalEntityRecord[]
        {
            new() { Code = "ENT-US", Name = "US Inc", Headcount = 640, ControlsPassing = 58, ControlsTotal = 64 },
            new() { Code = "ENT-EU", Name = "EU GmbH", Headcount = 310, ControlsPassing = 49, ControlsTotal = 54 },
            new() { Code = "ENT-FED", Name = "Federal", Headcount = 130, ControlsPassing = 36, ControlsTotal = 46 },
        };

        var summary = EntityRollupCalculator.Summarize(entities);

        Assert.Equal(3, summary.EntityCount);
        Assert.Equal(1080, summary.TotalHeadcount);
        Assert.Equal(143, summary.ControlsPassing);
        Assert.Equal(164, summary.ControlsTotal);
        Assert.Equal(87, summary.ConsolidatedReadinessPercent);
    }

    [Fact]
    public void PhishingMetrics_calculates_rates_and_risk_bands()
    {
        Assert.Equal(1.3, PhishingMetrics.RatePercent(14, 1080));
        Assert.Equal(75.6, PhishingMetrics.RatePercent(816, 1080));
        Assert.Equal("Low", PhishingMetrics.RiskBand(1.3));
        Assert.Equal("Medium", PhishingMetrics.RiskBand(5.5));
        Assert.Equal("High", PhishingMetrics.RiskBand(12.0));
    }

    [Theory]
    [InlineData("Terraform", "aws_vpc_security_group_ingress_rule")]
    [InlineData("Dockerfile", "USER nonroot:nonroot")]
    [InlineData("GitHubWorkflow", "environment: production-approved")]
    [InlineData("IamPolicy", "aws:MultiFactorAuthPresent")]
    public void RemediationPrBuilder_generates_sanitized_branch_and_unified_diff(string artifactType, string expectedFragment)
    {
        var patch = RemediationPrBuilder.Generate("CFG-01", "sg-staging \"mongo\"\n", "harborline/infra", artifactType);

        Assert.StartsWith("d2d/fix-cfg-01-", patch.BranchName, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", patch.BranchName, StringComparison.Ordinal);
        Assert.Contains("--- a/", patch.UnifiedDiff, StringComparison.Ordinal);
        Assert.Contains("+++ b/", patch.UnifiedDiff, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, patch.UnifiedDiff, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeMachineCalculator_compares_two_dates_across_check_weeks_and_evidence_chain()
    {
        var weeks = new CheckWeek[]
        {
            new() { Id = Guid.NewGuid(), WeekOf = new DateOnly(2026, 8, 1), Passing = 10, Failing = 6 },
            new() { Id = Guid.NewGuid(), WeekOf = new DateOnly(2026, 11, 15), Passing = 15, Failing = 1 },
        };
        var chain = new ChainEntry[]
        {
            new() { Id = Guid.NewGuid(), Sequence = 1, Head = "head1", CreatedAt = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero) },
            new() { Id = Guid.NewGuid(), Sequence = 2, Head = "head2", CreatedAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero) },
            new() { Id = Guid.NewGuid(), Sequence = 3, Head = "head3", CreatedAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero) },
        };

        var diff = TimeMachineCalculator.Compare(new DateOnly(2026, 8, 5), new DateOnly(2026, 11, 20), weeks, chain, 19, 20);

        Assert.Equal(63, diff.AsOf.TestPassPercent);
        Assert.Equal(94, diff.CompareTo.TestPassPercent);
        Assert.Equal(1, diff.AsOf.ChainEntryCount);
        Assert.Equal(3, diff.CompareTo.ChainEntryCount);
        Assert.Equal(2, diff.ChainEntriesDelta);
        Assert.Equal("head1", diff.AsOf.LatestChainHead);
        Assert.Equal("head3", diff.CompareTo.LatestChainHead);
        Assert.True(diff.ReadinessDelta > 0);
    }
}
