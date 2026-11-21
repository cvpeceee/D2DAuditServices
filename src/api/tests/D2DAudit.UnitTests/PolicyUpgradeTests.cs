using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class PolicyUpgradeTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    [Fact]
    public void Diff_marks_added_removed_and_unchanged_lines()
    {
        var lines = LineDiff.Compute("a\r\nb\nc", "a\nc\nd");

        Assert.Equal(
            [("same", "a"), ("removed", "b"), ("same", "c"), ("added", "d")],
            lines.Select(item => (item.Kind, item.Text)));
        Assert.Equal((2, (int?)null), (lines[1].OldNumber!.Value, lines[1].NewNumber));
        Assert.Equal(((int?)null, 3), (lines[3].OldNumber, lines[3].NewNumber!.Value));
    }

    [Fact]
    public void Diff_handles_empty_sides()
    {
        Assert.Empty(LineDiff.Compute(null, ""));
        Assert.All(LineDiff.Compute("", "x\ny"), item => Assert.Equal("added", item.Kind));
        Assert.All(LineDiff.Compute("x\ny", null), item => Assert.Equal("removed", item.Kind));
    }

    [Fact]
    public void Diff_caps_very_long_documents()
    {
        var big = string.Join("\n", Enumerable.Range(0, LineDiff.MaxLines + 500));
        var lines = LineDiff.Compute(big, big);
        Assert.Equal(LineDiff.MaxLines, lines.Count);
        Assert.All(lines, item => Assert.Equal("same", item.Kind));
    }

    [Fact]
    public void Attestations_renew_annually()
    {
        var renewOn = AttestationRules.RenewOn(Today);
        Assert.Equal(new DateOnly(2027, 10, 21), renewOn);
        Assert.Equal("current", AttestationRules.Status(AttestationRules.Active, renewOn, Today));
        Assert.Equal("renewalDue", AttestationRules.Status(AttestationRules.Active, renewOn, renewOn.AddDays(-30)));
        Assert.Equal("overdue", AttestationRules.Status(AttestationRules.Active, renewOn, renewOn.AddDays(1)));
        Assert.Equal("renewed", AttestationRules.Status(AttestationRules.Renewed, renewOn, renewOn.AddDays(1)));
    }

    [Fact]
    public void Groups_match_departments_or_everyone()
    {
        Assert.True(AttestationRules.InGroup(AttestationRules.Everyone, "Sales"));
        Assert.True(AttestationRules.InGroup("engineering", "Engineering"));
        Assert.False(AttestationRules.InGroup("Engineering", "Sales"));
        Assert.Equal("invalid_group", AttestationRules.Validate(" ", Today, Today).Error);
        Assert.Equal("invalid_due", AttestationRules.Validate("Everyone", Today.AddDays(-1), Today).Error);
        Assert.True(AttestationRules.Validate("Everyone", Today, Today).Succeeded);
    }

    [Fact]
    public void Template_library_has_unique_short_templates_mapped_to_controls()
    {
        var all = PolicyTemplateLibrary.All;
        Assert.Equal(23, all.Count);
        Assert.Equal(all.Count, all.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, item =>
        {
            Assert.StartsWith("POL-", item.Code);
            Assert.NotEmpty(item.ControlCodes);
            Assert.InRange(item.Body.Length, 50, 4000);
        });
        Assert.Contains(all, item => item.Code == "POL-AIU");
    }
}
