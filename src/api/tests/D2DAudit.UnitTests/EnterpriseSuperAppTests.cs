using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class EnterpriseSuperAppTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TrustReceiptBuilder_produces_deterministic_verifiable_sha256_receipt()
    {
        var frameworks = new TrustReceiptFramework[]
        {
            new("soc2", "SOC 2", 18, 20, 90),
            new("iso27001", "ISO 27001", 80, 93, 86),
        };

        var receipt = TrustReceiptBuilder.Build(
            "harborline",
            "Harborline",
            "US",
            6,
            Now,
            frameworks,
            42,
            42,
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");

        Assert.Equal("Verified", receipt.ChainStatus);
        Assert.Equal(64, receipt.ReceiptSha256.Length);
        Assert.True(TrustReceiptBuilder.Verify(receipt.CanonicalPayload, receipt.ReceiptSha256));
        Assert.False(TrustReceiptBuilder.Verify(receipt.CanonicalPayload + "|tampered", receipt.ReceiptSha256));
        Assert.False(TrustReceiptBuilder.Verify("", receipt.ReceiptSha256));
    }

    [Fact]
    public void FrameworkSimulator_computes_overlap_readiness_and_unmapped_gap_clauses()
    {
        var leaves = new SimulatorRequirementInput[]
        {
            new("PCI-1.2", "Network rules", "Deny untrusted traffic.", Mapped: true, Passing: true),
            new("PCI-1.3", "Isolate CDE", "Separate subnets.", Mapped: false, Passing: false),
            new("PCI-8.4", "MFA everywhere", "Enforce MFA.", Mapped: true, Passing: false),
            new("PCI-3.2", "Retention limits", "Purge stale data.", Mapped: false, Passing: false),
        };

        var sim = FrameworkSimulator.Analyze("pci-dss", "PCI DSS 4.0", leaves);

        Assert.Equal(4, sim.TotalRequirements);
        Assert.Equal(2, sim.CoveredRequirements);
        Assert.Equal(1, sim.PassingRequirements);
        Assert.Equal(50, sim.OverlapPercent);
        Assert.Equal(25, sim.ReadinessPercent);
        Assert.Equal(2, sim.Gaps.Count);
        Assert.Equal("PCID-01", sim.Gaps[0].SuggestedControlCode);
        Assert.Equal("PCI-1.3", sim.Gaps[0].Code);
        Assert.Equal("PCID-02", sim.Gaps[1].SuggestedControlCode);
    }

    [Fact]
    public void CustomFrameworkCsv_parses_quoted_rows_and_replaces_shall()
    {
        const string csv = """
        code,title,summary
        CUST-01,Zero Trust Access,"Systems shall verify identity on every request, including contractors."
        CUST-02,Immutable Backups,Backups are immutable and tested quarterly.
        CUST-01,Duplicate Code,Should be skipped
        """;

        var rows = CustomFrameworkCsv.Parse(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("CUST-01", rows[0].Code);
        Assert.DoesNotContain("shall", rows[0].Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("including contractors", rows[0].Summary, StringComparison.Ordinal);
        Assert.Equal("CUST-02", rows[1].Code);
    }

    [Fact]
    public void GroundedAssistant_cites_matching_workspace_facts_and_routes()
    {
        var facts = new AssistantFact[]
        {
            new("Exposure", "CFG-01", "AWS sg-staging-analytics-mongo (MongoDB:27017)", "Open Critical exposure from 0.0.0.0/0.", "/exposure"),
            new("FinOps", "CR-02", "harborline-staging-exports ($640/mo)", "Missing S3 lifecycle policy retains stale PII.", "/finops"),
            new("AttackPath", "AP-01", "Internet MongoDB to staging-exports", "Blast radius 98/100 (Critical).", "/attack-paths"),
        };

        var reply = GroundedAssistant.Answer("Which attack paths or finops savings affect staging-exports?", facts, "Harborline");

        Assert.Equal("grounded-retrieval", reply.Mode);
        Assert.NotEmpty(reply.Citations);
        Assert.Contains(reply.Citations, item => item.Code == "AP-01");
        Assert.Contains("/attack-paths", reply.SuggestedRoutes);
        Assert.Contains("/finops", reply.SuggestedRoutes);
    }

    [Fact]
    public void GroundedAssistant_handles_empty_question_safely()
    {
        var reply = GroundedAssistant.Answer("   ", [], "workspace");
        Assert.Equal("grounded-retrieval", reply.Mode);
        Assert.NotEmpty(reply.SuggestedRoutes);
    }
}
