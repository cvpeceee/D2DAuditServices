using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class CheckAndChainTests
{
    [Fact]
    public void Chain_head_changes_when_the_bytes_change()
    {
        var tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var first = EvidenceChain.ContentHash("one"u8);
        var second = EvidenceChain.ContentHash("two"u8);
        var head = EvidenceChain.NextHead(null, first, EvidenceChain.Canonical(tenant, tenant.ToString("N") + "/a.parquet", first, "MFA"));
        var next = EvidenceChain.NextHead(head, second, EvidenceChain.Canonical(tenant, tenant.ToString("N") + "/b.parquet", second, "MFA"));
        Assert.NotEqual(first, second);
        Assert.NotEqual(head, next);
        Assert.Equal(64, head.Length);
    }

    [Fact]
    public void Acme_shaped_resources_fail_the_public_bucket_and_pass_cloudtrail()
    {
        var resources = new NormalizedResource[]
        {
            new("aws", "bucket", "logs-prod", "logs-prod", Flags(("public", false), ("encrypted", true))),
            new("aws", "bucket", "marketing-site", "marketing-site", Flags(("public", true), ("encrypted", false))),
            new("aws", "trail", "management-events", "management-events", Flags(("enabled", true))),
            new("aws", "root", "111122223333", "root:111122223333", Flags(("mfa", true))),
        };

        var publicAccess = AutomatedChecks.Evaluate("s3-public-access", resources);
        var trail = AutomatedChecks.Evaluate("cloudtrail-enabled", resources);
        var root = AutomatedChecks.Evaluate("root-mfa", resources);
        Assert.Equal(TestOutcome.Fail, publicAccess.Outcome);
        Assert.Contains("marketing-site", publicAccess.Detail, StringComparison.Ordinal);
        Assert.Equal(TestOutcome.Pass, trail.Outcome);
        Assert.Equal(TestOutcome.Pass, root.Outcome);
    }

    [Fact]
    public void A_leaver_who_is_still_active_in_another_system_fails()
    {
        var resources = new NormalizedResource[]
        {
            new("google", "user", "sam", "Sam Leaved", new Dictionary<string, string>
            {
                ["email"] = "sam.leaved@acme.example",
                ["mfa"] = "true",
                ["offboarded"] = "true",
                ["status"] = "suspended",
            }),
            new("github", "user", "sam-gh", "Sam Leaved", new Dictionary<string, string>
            {
                ["email"] = "sam.leaved@acme.example",
                ["mfa"] = "true",
                ["offboarded"] = "false",
                ["status"] = "active",
            }),
        };

        var result = AutomatedChecks.Evaluate("offboarded-removed", resources);
        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Contains("Sam Leaved", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_resource_kinds_do_not_count_as_a_failure()
    {
        var result = AutomatedChecks.Evaluate("branch-protection", []);
        Assert.Equal(TestOutcome.NotRun, result.Outcome);
    }

    [Fact]
    public void Unknown_checks_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AutomatedChecks.Evaluate("not-a-check", []));
        Assert.Contains("Unknown automated check", exception.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Flags(params (string Name, bool Value)[] flags)
    {
        var values = new Dictionary<string, string>();
        foreach (var (name, value) in flags)
        {
            values[name] = value ? "true" : "false";
        }

        return values;
    }
}
