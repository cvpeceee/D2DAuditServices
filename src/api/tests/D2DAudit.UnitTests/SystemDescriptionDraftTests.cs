using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class SystemDescriptionDraftTests
{
    [Fact]
    public void The_draft_names_the_company_and_the_control_count()
    {
        var body = SystemDescriptionDraft.Compose(
            "Harborline",
            "US",
            "A multi-cloud data platform.",
            ["SOC 2", "ISO 27001"],
            40,
            28,
            ["AWS", "GitHub"],
            12,
            4);
        Assert.Contains("Harborline", body, StringComparison.Ordinal);
        Assert.Contains("28 of 40 controls are passing", body, StringComparison.Ordinal);
        Assert.Contains("SOC 2, ISO 27001", body, StringComparison.Ordinal);
        Assert.Contains("AWS, GitHub", body, StringComparison.Ordinal);
    }
}
