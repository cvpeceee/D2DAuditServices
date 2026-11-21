using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class EvidenceLibraryTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    [Theory]
    [InlineData(EvidenceCadence.Monthly, "2026-11-21")]
    [InlineData(EvidenceCadence.Quarterly, "2027-01-21")]
    [InlineData(EvidenceCadence.Semiannual, "2027-04-21")]
    [InlineData(EvidenceCadence.Annual, "2027-10-21")]
    public void Cadence_sets_the_next_expiry(EvidenceCadence cadence, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), EvidenceExpiry.NextValidUntil(Today, cadence));
    }

    [Fact]
    public void One_time_evidence_never_expires()
    {
        Assert.Null(EvidenceExpiry.NextValidUntil(Today, EvidenceCadence.OneTime));
        Assert.Equal(EvidenceExpiry.Current, EvidenceExpiry.Status(true, null, Today));
    }

    [Fact]
    public void Status_moves_from_current_to_expiring_soon_to_expired()
    {
        Assert.Equal(EvidenceExpiry.Missing, EvidenceExpiry.Status(false, Today.AddYears(1), Today));
        Assert.Equal(EvidenceExpiry.Current, EvidenceExpiry.Status(true, Today.AddDays(31), Today));
        Assert.Equal(EvidenceExpiry.ExpiringSoon, EvidenceExpiry.Status(true, Today.AddDays(30), Today));
        Assert.Equal(EvidenceExpiry.ExpiringSoon, EvidenceExpiry.Status(true, Today, Today));
        Assert.Equal(EvidenceExpiry.Expired, EvidenceExpiry.Status(true, Today.AddDays(-1), Today));
    }

    [Fact]
    public void Renewal_is_needed_unless_current()
    {
        Assert.False(EvidenceExpiry.NeedsRenewal(EvidenceExpiry.Current));
        Assert.True(EvidenceExpiry.NeedsRenewal(EvidenceExpiry.ExpiringSoon));
        Assert.True(EvidenceExpiry.NeedsRenewal(EvidenceExpiry.Expired));
        Assert.True(EvidenceExpiry.NeedsRenewal(EvidenceExpiry.Missing));
    }

    [Fact]
    public void Library_blocks_readiness_only_when_every_linked_item_has_lapsed()
    {
        Assert.False(EvidenceExpiry.LibraryBlocksReadiness([]));
        Assert.True(EvidenceExpiry.LibraryBlocksReadiness([EvidenceExpiry.Expired]));
        Assert.True(EvidenceExpiry.LibraryBlocksReadiness([EvidenceExpiry.Expired, EvidenceExpiry.Missing]));
        Assert.False(EvidenceExpiry.LibraryBlocksReadiness([EvidenceExpiry.Expired, EvidenceExpiry.ExpiringSoon]));
    }

    [Fact]
    public void Passing_controls_with_only_expired_library_evidence_become_not_ready()
    {
        Assert.Equal(ControlStatus.NeedsEvidence, ControlReadiness.ApplyLibrary(ControlStatus.Passing, [EvidenceExpiry.Expired]));
        Assert.Equal(ControlStatus.Passing, ControlReadiness.ApplyLibrary(ControlStatus.Passing, [EvidenceExpiry.Current]));
        Assert.Equal(ControlStatus.Failing, ControlReadiness.ApplyLibrary(ControlStatus.Failing, [EvidenceExpiry.Expired]));
    }

    [Fact]
    public void Evidence_requests_need_an_assignee_and_a_future_due_date()
    {
        Assert.True(EvidenceRequestRules.Validate("Ana", "ana@harborline.example", Today, Today).Succeeded);
        Assert.Equal("invalid_assignee", EvidenceRequestRules.Validate(" ", "ana@harborline.example", Today, Today).Error);
        Assert.Equal("invalid_assignee", EvidenceRequestRules.Validate("Ana", "not-an-email", Today, Today).Error);
        Assert.Equal("invalid_due", EvidenceRequestRules.Validate("Ana", "ana@harborline.example", Today.AddDays(-1), Today).Error);
        Assert.True(EvidenceRequestRules.IsOverdue(EvidenceRequestRules.Open, Today.AddDays(-1), Today));
        Assert.False(EvidenceRequestRules.IsOverdue(EvidenceRequestRules.Fulfilled, Today.AddDays(-1), Today));
    }

    [Fact]
    public void Readiness_lists_what_is_missing()
    {
        var report = ControlReadiness.Evaluate(new ReadinessInput(null, [TestOutcome.Fail, TestOutcome.Pass], 0, 1, false, [EvidenceExpiry.Expired]));

        Assert.False(report.Ready);
        Assert.Equal(0, report.Percent);
        Assert.Equal(["owner", "tests", "evidence", "library"], report.Checks.Select(item => item.Key));
        Assert.All(report.Checks, item => Assert.False(item.Met));
        Assert.Contains("1 test(s) failing", report.Checks[1].Detail);
        Assert.Contains("1 more evidence", report.Checks[2].Detail);
    }

    [Fact]
    public void Readiness_is_complete_when_every_check_is_met()
    {
        var report = ControlReadiness.Evaluate(new ReadinessInput("owner@harborline.example", [TestOutcome.Pass, TestOutcome.NotRun], 2, 1, true, [EvidenceExpiry.Expired, EvidenceExpiry.Current]));
        Assert.True(report.Ready);
        Assert.Equal(100, report.Percent);
    }

    [Fact]
    public void Readiness_can_require_a_passing_test()
    {
        var report = ControlReadiness.Evaluate(new ReadinessInput("o@x.example", [TestOutcome.NotRun], 0, 0, true, []));
        Assert.False(report.Ready);
        Assert.Equal(66, report.Percent);
        Assert.Equal(3, report.Checks.Count);
    }

    [Fact]
    public void Comments_must_be_present_and_bounded()
    {
        Assert.True(ControlReadiness.ValidateComment("Looks good").Succeeded);
        Assert.Equal("invalid_comment", ControlReadiness.ValidateComment("  ").Error);
        Assert.Equal("invalid_comment", ControlReadiness.ValidateComment(new string('x', 2001)).Error);
    }
}
