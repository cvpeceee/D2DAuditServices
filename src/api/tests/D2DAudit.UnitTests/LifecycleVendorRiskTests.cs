using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class LifecycleVendorRiskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Offboarding_passes_when_access_is_revoked_within_the_sla()
    {
        var triggered = Now.AddDays(-3);
        var result = LifecycleRules.EvaluateOffboarding(
            [
                new ChecklistSlaSnapshot("Winter Cruz", LifecycleKind.Offboarding, triggered, triggered.AddHours(72), triggered.AddHours(24), triggered.AddHours(5), triggered.AddHours(30)),
            ],
            Now,
            24);
        Assert.Equal(TestOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Offboarding_fails_when_access_is_revoked_late_or_still_open_past_the_sla()
    {
        var triggered = Now.AddDays(-3);
        var result = LifecycleRules.EvaluateOffboarding(
            [
                new ChecklistSlaSnapshot("Late Larry", LifecycleKind.Offboarding, triggered, triggered.AddHours(72), triggered.AddHours(24), triggered.AddHours(30), null),
                new ChecklistSlaSnapshot("Sam Leaved", LifecycleKind.Offboarding, triggered, triggered.AddHours(72), triggered.AddHours(24), null, null),
                new ChecklistSlaSnapshot("Fresh Fay", LifecycleKind.Offboarding, Now.AddHours(-2), Now.AddHours(70), Now.AddHours(22), null, null),
            ],
            Now,
            24);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("Late Larry", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Sam Leaved", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Fresh Fay", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Onboarding_fails_only_for_hires_past_the_window()
    {
        var result = LifecycleRules.EvaluateOnboarding(
            [
                new ChecklistSlaSnapshot("Dev Patel", LifecycleKind.Onboarding, Now.AddDays(-4), Now.AddDays(10), null, null, null),
                new ChecklistSlaSnapshot("Old Olive", LifecycleKind.Onboarding, Now.AddDays(-20), Now.AddDays(-6), null, null, null),
                new ChecklistSlaSnapshot("Done Dana", LifecycleKind.Onboarding, Now.AddDays(-20), Now.AddDays(-6), null, null, Now.AddDays(-10)),
            ],
            Now,
            14);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("Old Olive", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev Patel", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Done Dana", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Offboarding_checklist_has_one_step_per_active_account_and_respects_groups()
    {
        var accounts = new[]
        {
            new LifecycleAccount("Slack", "sam", true),
            new LifecycleAccount("Figma", "sam@x", true),
            new LifecycleAccount("Zoom", "old", false),
        };
        var engineering = LifecycleRules.Generate(LifecycleKind.Offboarding, LifecycleRules.DefaultTemplates, ["everyone", "engineering"], accounts, Now, 24, 72, 14);
        var sales = LifecycleRules.Generate(LifecycleKind.Offboarding, LifecycleRules.DefaultTemplates, ["everyone"], accounts, Now, 24, 72, 14);

        Assert.Equal(2, engineering.Count(step => step.Code == "revoke-app"));
        Assert.DoesNotContain(engineering, step => step.Provider == "Zoom");
        Assert.Contains(engineering, step => step.Code == "remove-github");
        Assert.DoesNotContain(sales, step => step.Code == "remove-github");
        Assert.All(engineering.Where(step => step.RevokesAccess), step => Assert.Equal(Now.AddHours(24), step.DueAt));
        Assert.All(engineering, step => Assert.True(step.DueAt <= Now.AddHours(72)));
    }

    [Fact]
    public void Groups_match_by_department_or_role_keyword()
    {
        Assert.Equal(["everyone", "engineering"], LifecycleRules.GroupsFor(LifecycleRules.DefaultGroups, "Engineering", "Staff"));
        Assert.Equal(["everyone", "contractors"], LifecycleRules.GroupsFor(LifecycleRules.DefaultGroups, "Sales", "Contractor"));
        Assert.Equal(["everyone"], LifecycleRules.GroupsFor(LifecycleRules.DefaultGroups, "Finance", "Analyst"));
    }

    [Theory]
    [InlineData(-1, "Missed")]
    [InlineData(1, "Met")]
    public void Sla_state_reports_met_or_missed_once_done(int hoursEarly, string expected)
    {
        var due = Now;
        Assert.Equal(expected, LifecycleRules.SlaState(due, due.AddHours(-hoursEarly), Now.AddDays(1)));
        Assert.Equal("Overdue", LifecycleRules.SlaState(due, null, Now.AddMinutes(1)));
        Assert.Equal("DueSoon", LifecycleRules.SlaState(due, null, Now.AddHours(-2)));
        Assert.Equal("OnTrack", LifecycleRules.SlaState(due, null, Now.AddDays(-2)));
    }

    [Fact]
    public void Inherent_risk_combines_criticality_data_and_subprocessor_into_tiers()
    {
        var high = VendorRiskScoring.Inherent(VendorCriticality.Critical, ["Financial", "CustomerData"], true);
        var low = VendorRiskScoring.Inherent(VendorCriticality.Low, ["None"], false);
        var mid = VendorRiskScoring.Inherent(VendorCriticality.Medium, ["PersonalData"], false);

        Assert.Equal(100, high.Score);
        Assert.Equal("Tier1", high.Tier);
        Assert.Equal(6, high.DefaultCadenceMonths);
        Assert.Equal("Tier4", low.Tier);
        Assert.Equal(24, low.DefaultCadenceMonths);
        Assert.Equal(45, mid.Score);
        Assert.Equal("Tier3", mid.Tier);
    }

    [Fact]
    public void Categories_are_inferred_from_free_text()
    {
        Assert.Contains("Financial", VendorRiskScoring.InferCategories("Card payments and customer billing"));
        Assert.Equal(["None"], VendorRiskScoring.InferCategories("Fonts only"));
    }

    [Fact]
    public void Review_and_renewal_status_use_cadence_and_windows()
    {
        var today = new DateOnly(2026, 11, 14);
        var overdue = VendorRiskScoring.Review(today.AddMonths(-13), 12, today.AddYears(1), today);
        var fallback = VendorRiskScoring.Review(null, 12, today.AddDays(10), today);
        var current = VendorRiskScoring.Review(today.AddMonths(-1), 12, today, today);

        Assert.Equal("Overdue", overdue.State);
        Assert.Equal("DueSoon", fallback.State);
        Assert.Equal(today.AddDays(10), fallback.NextReviewOn);
        Assert.Equal("Current", current.State);
        Assert.Equal("Lapsed", VendorRiskScoring.RenewalState(today.AddDays(-1), today));
        Assert.Equal("Upcoming", VendorRiskScoring.RenewalState(today.AddDays(45), today));
        Assert.Equal("Current", VendorRiskScoring.RenewalState(today.AddDays(90), today));
        Assert.Equal("None", VendorRiskScoring.RenewalState(null, today));
    }

    [Fact]
    public void Questionnaire_scoring_flags_gaps_and_a_critical_no_forces_high_risk()
    {
        var template = VendorQuestionnaireLibrary.Core;
        var allYes = template.Questions.ToDictionary(item => item.Code, _ => "Yes");
        var perfect = VendorQuestionnaireLibrary.Score(template, allYes);
        Assert.Equal(100, perfect.Percent);
        Assert.Equal("Low", perfect.Rating);
        Assert.Equal(20, perfect.Total);
        Assert.Equal(20, perfect.Answered);

        var noMfa = new Dictionary<string, string>(allYes) { ["AC-1"] = "No" };
        var withGap = VendorQuestionnaireLibrary.Score(template, noMfa);
        Assert.True(withGap.Percent >= 85);
        Assert.Equal("High", withGap.Rating);
        Assert.Equal(["AC-1"], withGap.Flagged);

        var partial = VendorQuestionnaireLibrary.Score(template, new Dictionary<string, string> { ["GOV-1"] = "Yes", ["GOV-2"] = "Maybe" });
        Assert.Equal(1, partial.Answered);
    }

    [Fact]
    public void Portal_tokens_verify_in_constant_time_and_expire()
    {
        var tenant = Guid.NewGuid();
        var (token, hash) = VendorPortalTokens.Create(tenant);

        Assert.True(VendorPortalTokens.TryParse(token, out var parsedTenant, out var secret));
        Assert.Equal(tenant, parsedTenant);
        Assert.Equal(PortalTokenState.Valid, VendorPortalTokens.Verify(hash, secret, Now.AddDays(1), Now));
        Assert.Equal(PortalTokenState.Expired, VendorPortalTokens.Verify(hash, secret, Now, Now));
        Assert.Equal(PortalTokenState.Invalid, VendorPortalTokens.Verify(hash, secret[..^1] + (secret[^1] == 'a' ? 'b' : 'a'), Now.AddDays(1), Now));
        Assert.False(VendorPortalTokens.TryParse("not-a-token", out _, out _));
        Assert.False(VendorPortalTokens.TryParse($"{tenant:N}.short", out _, out _));
        Assert.False(VendorPortalTokens.TryParse($"{tenant:N}.{new string('a', 40)}/../x", out _, out _));
    }

    [Fact]
    public void Soc2_summary_extracts_period_opinion_exceptions_and_cuecs()
    {
        const string report = """
            SOC 2 Type II report
            For the period from January 1, 2026 to June 30, 2026.
            In our opinion, the description presents fairly, in all material respects, the system.
            Exception noted: one of 25 sampled terminations had access removed after 5 days.
            Complementary user entity controls:
            - Customers review user access quarterly.
            - Customers protect their API keys.
            Section 5:
            """;
        var summary = Soc2Summarizer.Summarize(report);

        Assert.Equal("SOC 2 Type II", summary.ReportType);
        Assert.Equal("January 1, 2026 to June 30, 2026", summary.ReportPeriod);
        Assert.Equal("Unqualified", summary.Opinion);
        Assert.Single(summary.Exceptions);
        Assert.Equal(2, summary.Cuecs.Count);

        var clean = Soc2Summarizer.Summarize("SOC 2 Type I as of March 31, 2026. No exceptions noted. We do not express an opinion.");
        Assert.Empty(clean.Exceptions);
        Assert.Equal("As of March 31, 2026", clean.ReportPeriod);
        Assert.Equal("Disclaimer", clean.Opinion);
    }
}
