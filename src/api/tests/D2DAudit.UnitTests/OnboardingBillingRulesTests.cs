using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class OnboardingBillingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signup_validation_accepts_a_work_email_and_strong_password()
    {
        var errors = SignupRules.Validate(new SignupInput("Northwind Labs", "Ana@Northwind.io", "correct-horse-42", "EU", PasswordRequired: true));

        Assert.Empty(errors);
    }

    [Fact]
    public void Signup_validation_reports_each_problem()
    {
        var errors = SignupRules.Validate(new SignupInput("N", "not-an-email", "short", "APAC", PasswordRequired: true));

        Assert.Equal(["invalid_company", "invalid_email", "invalid_region", "weak_password"], errors);
    }

    [Theory]
    [InlineData("ana@mailinator.com", "disposable_email")]
    [InlineData("ana@inbox.mailinator.com", "disposable_email")]
    [InlineData("ana@gmail.com", "work_email_required")]
    public void Signup_validation_rejects_disposable_and_free_mail_domains(string email, string expected)
    {
        var errors = SignupRules.Validate(new SignupInput("Northwind", email, null, "US", PasswordRequired: false));

        Assert.Equal([expected], errors);
    }

    [Fact]
    public void Password_is_only_required_for_local_identity()
    {
        Assert.Empty(SignupRules.Validate(new SignupInput("Northwind", "ana@northwind.io", null, "US", PasswordRequired: false)));
        Assert.False(SignupRules.StrongPassword("onlyletterslong"));
        Assert.False(SignupRules.StrongPassword("12345678901234"));
    }

    [Fact]
    public void Slugs_are_url_safe_and_start_with_a_letter()
    {
        Assert.Equal("acme-widgets-co", SignupRules.SlugFor("  Acme Widgets & Co. "));
        Assert.Equal("org-3m", SignupRules.SlugFor("3M"));
        Assert.Equal("Ana Lopez", SignupRules.DisplayNameFor("ana.lopez@northwind.io"));
    }

    [Fact]
    public void One_time_tokens_are_hashed_and_expire_at_the_boundary()
    {
        var token = OneTimeTokens.Create();
        var hash = OneTimeTokens.Hash(token);

        Assert.NotEqual(token, hash);
        Assert.True(OneTimeTokens.Matches(token, hash));
        Assert.False(OneTimeTokens.Matches(token + "x", hash));
        Assert.False(OneTimeTokens.IsExpired(Now.AddSeconds(1), Now));
        Assert.True(OneTimeTokens.IsExpired(Now, Now));
    }

    [Fact]
    public void Invite_state_prefers_accepted_then_revoked_then_expiry()
    {
        var invite = new MemberInvite { ExpiresAt = Now.Add(OneTimeTokens.InviteLifetime) };
        Assert.Equal("Pending", invite.State(Now));
        Assert.Equal("Expired", invite.State(Now.AddDays(8)));

        invite.RevokedAt = Now;
        Assert.Equal("Revoked", invite.State(Now));

        invite.AcceptedAt = Now;
        Assert.Equal("Accepted", invite.State(Now.AddDays(30)));
    }

    [Fact]
    public void Feature_gate_blocks_at_limit_and_suggests_the_smallest_plan_that_fits()
    {
        var starter = PlanCatalog.For(PlanCatalog.Starter);

        var underLimit = FeatureGate.Check(starter, PlanFeatures.Seats, 9);
        Assert.True(underLimit.Allowed);
        Assert.Null(underLimit.UpgradeTo);

        var atLimit = FeatureGate.Check(starter, PlanFeatures.Frameworks, 1);
        Assert.False(atLimit.Allowed);
        Assert.False(atLimit.OverLimit);
        Assert.Equal(PlanCatalog.Growth, atLimit.UpgradeTo);

        var overGrowth = FeatureGate.Check(starter, PlanFeatures.Frameworks, 6);
        Assert.True(overGrowth.OverLimit);
        Assert.Equal(PlanCatalog.Enterprise, overGrowth.UpgradeTo);

        var trust = FeatureGate.Check(starter, PlanFeatures.TrustCenter);
        Assert.False(trust.Allowed);
        Assert.Equal(PlanCatalog.Growth, trust.UpgradeTo);
    }

    [Fact]
    public void Enterprise_is_unlimited_and_unknown_plans_fall_back_to_starter()
    {
        var enterprise = PlanCatalog.For(PlanCatalog.Enterprise);

        Assert.True(FeatureGate.Check(enterprise, PlanFeatures.Seats, 5_000).Allowed);
        Assert.Equal(PlanCatalog.Starter, PlanCatalog.For("Platinum").Plan);
        Assert.Throws<ArgumentOutOfRangeException>(() => FeatureGate.Check(enterprise, "rockets"));
    }

    [Fact]
    public void Heatmap_has_25_cells_and_clamps_out_of_scale_values()
    {
        var map = RiskHeatmap.Build([
            ("R-1", 5, 5, 2, 3),
            ("R-2", 5, 4, 2, 3),
            ("R-3", 9, 0, 1, 1),
        ]);

        Assert.Equal(25, map.Inherent.Count);
        Assert.Equal(25, map.Residual.Count);
        Assert.Equal(3, map.Total);
        Assert.Equal(2, map.InherentCritical);
        Assert.Equal(0, map.ResidualCritical);
        Assert.Equal(["R-3"], map.Inherent.Single(cell => cell is { Likelihood: 5, Impact: 1 }).RiskCodes);
        Assert.Equal(2, map.Residual.Single(cell => cell is { Likelihood: 2, Impact: 3 }).Count);
        Assert.Equal(5, map.Inherent[0].Likelihood);
    }

    [Fact]
    public void Annualized_loss_expectancy_multiplies_sle_by_aro()
    {
        Assert.Equal(62_500.00m, RiskQuantification.AnnualizedLossExpectancy(250_000m, 0.25m));
        Assert.Null(RiskQuantification.AnnualizedLossExpectancy(250_000m, null));
        Assert.Null(RiskQuantification.AnnualizedLossExpectancy(-1m, 1m));
        Assert.Equal(new DateOnly(2026, 9, 1), RiskCadence.NextReview(new DateOnly(2026, 3, 1), "SemiAnnual"));
    }

    [Fact]
    public void Risk_library_offers_about_forty_risks_with_suggested_controls()
    {
        Assert.InRange(RiskLibrary.All.Count, 40, 50);
        Assert.All(RiskLibrary.All, entry => Assert.NotEmpty(entry.Controls));
        Assert.Equal(RiskLibrary.All.Count, RiskLibrary.All.Select(entry => entry.Code).Distinct().Count());
    }

    [Fact]
    public void Readiness_plan_rejects_short_targets_and_orders_milestones()
    {
        var today = new DateOnly(2026, 3, 1);
        Assert.True(ReadinessPlan.TooSoon(today, today.AddDays(29)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadinessPlan.Generate(today, today.AddDays(10), ["soc2"]));

        var target = today.AddDays(180);
        var plan = ReadinessPlan.Generate(today, target, ["soc2"]);

        Assert.Equal(6, plan.Count);
        Assert.Equal(target, plan[^1].DueOn);
        Assert.Equal(plan.Select(item => item.DueOn).Order(), plan.Select(item => item.DueOn));
        Assert.All(plan.SelectMany(item => item.Tasks), task => Assert.InRange(task.DueOn, today, target));
        Assert.Contains("(SOC2)", plan[0].Tasks[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Recommender_maps_profile_to_frameworks()
    {
        var result = FrameworkRecommender.Recommend(new CompanyProfile("51-200", "fintech", ["aws"], ["payment_cards", "eu_personal_data"]));
        var keys = result.Frameworks.Select(item => item.FrameworkKey).ToArray();

        Assert.Equal("soc2", keys[0]);
        Assert.Contains("iso27001", keys);
        Assert.Contains("pci-dss", keys);
        Assert.Contains("gdpr", keys);
        Assert.Contains("dora", keys);
        Assert.Contains("CFG-01", result.ScopedControls);

        var small = FrameworkRecommender.Recommend(new CompanyProfile("1-10", "saas", [], []));
        Assert.Equal(["soc2", "nist-csf"], small.Frameworks.Select(item => item.FrameworkKey));
    }

    [Fact]
    public void Stripe_signature_accepts_valid_and_rejects_tampered_or_stale_payloads()
    {
        const string secret = "whsec_test";
        const string payload = "{\"id\":\"evt_1\"}";
        var timestamp = Now.ToUnixTimeSeconds();
        var header = $"t={timestamp},v1={StripeSignature.Compute(payload, timestamp, secret)}";

        Assert.True(StripeSignature.Verify(payload, header, secret, Now));
        Assert.False(StripeSignature.Verify(payload + " ", header, secret, Now));
        Assert.False(StripeSignature.Verify(payload, header, "whsec_other", Now));
        Assert.False(StripeSignature.Verify(payload, header, secret, Now.AddMinutes(6)));
        Assert.False(StripeSignature.Verify(payload, null, secret, Now));
    }
}
