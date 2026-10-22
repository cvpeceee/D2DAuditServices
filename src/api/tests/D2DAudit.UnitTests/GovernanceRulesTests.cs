using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class GovernanceRulesTests
{
    [Fact]
    public void Score_is_likelihood_times_impact_on_a_five_point_scale()
    {
        Assert.Equal(20, RiskScoring.Score(4, 5));
        Assert.Equal("critical", RiskScoring.Band(20));
        Assert.Equal("high", RiskScoring.Band(8));
        Assert.Equal("medium", RiskScoring.Band(4));
        Assert.Equal("low", RiskScoring.Band(3));
        Assert.True(RiskScoring.OnScale(1));
        Assert.False(RiskScoring.OnScale(6));
    }

    [Fact]
    public void A_leaver_with_a_live_account_is_flagged()
    {
        Assert.True(LeaverAccess.Flagged(PersonState.Offboarded, [false, true]));
        Assert.False(LeaverAccess.Flagged(PersonState.Offboarded, [false, false]));
        Assert.False(LeaverAccess.Flagged(PersonState.Active, [true]));
    }

    [Fact]
    public void A_review_completes_only_after_every_account_is_decided()
    {
        Assert.Equal("empty_campaign", AccessCampaignRules.Complete([]).Error);
        Assert.Equal("decisions_pending", AccessCampaignRules.Complete([AccessDecision.Approved, AccessDecision.Pending]).Error);
        Assert.True(AccessCampaignRules.Complete([AccessDecision.Approved, AccessDecision.Revoked]).Succeeded);
    }

    [Fact]
    public void Directory_sync_keeps_one_person_and_every_account()
    {
        var people = DirectorySync.People(
        [
            new DirectoryAccount("google", "sam", "Sam Leaved", "sam.leaved@acme.example", true, false),
            new DirectoryAccount("github", "sam-gh", "Sam Leaved", "Sam.Leaved@acme.example", false, true),
        ]);
        var person = Assert.Single(people);
        Assert.Equal(PersonState.Offboarded, person.State);
        Assert.Equal(2, person.Accounts.Count);
        Assert.Equal(DirectorySource.Mixed, DirectorySync.SourceOf(person.Accounts));
    }
}
