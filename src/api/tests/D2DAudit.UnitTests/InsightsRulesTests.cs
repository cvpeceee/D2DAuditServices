using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class InsightsRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_counts_controls_per_framework_vulns_and_overdue_items()
    {
        var soc2 = ("soc2", "SOC 2");
        var iso = ("iso27001", "ISO 27001");
        var input = new PostureInput(
            [
                new PostureControl(ControlStatus.Passing, [soc2, iso]),
                new PostureControl(ControlStatus.Failing, [soc2]),
                new PostureControl(ControlStatus.Passing, [iso]),
                new PostureControl(ControlStatus.NeedsEvidence, []),
            ],
            [Guid.NewGuid(), Guid.NewGuid()],
            ["Critical", "high", " HIGH ", "low", "unknown"],
            [Today.AddDays(-1), Today, Today.AddDays(3)],
            [Today.AddDays(-30), Today.AddDays(10)]);

        var snapshot = PostureRules.Compute(input, Today, Now);

        Assert.Equal(2, snapshot.ControlsPassing);
        Assert.Equal(4, snapshot.ControlsTotal);
        Assert.Equal(2, snapshot.FailingTests);
        Assert.Equal(1, snapshot.VulnCritical);
        Assert.Equal(2, snapshot.VulnHigh);
        Assert.Equal(0, snapshot.VulnMedium);
        Assert.Equal(1, snapshot.VulnLow);
        Assert.Equal(3, snapshot.OpenTasks);
        Assert.Equal(2, snapshot.OverdueItems);
        var frameworks = PostureRules.Frameworks(snapshot);
        Assert.Equal(new FrameworkPosture("iso27001", "ISO 27001", 2, 2), frameworks[0]);
        Assert.Equal(new FrameworkPosture("soc2", "SOC 2", 1, 2), frameworks[1]);
        Assert.Equal(2, PostureRules.FailingTests(snapshot).Count);
        Assert.Empty(PostureRules.FailingTests(null));
        Assert.Equal(50, PostureRules.Percent(2, 4));
        Assert.Equal(0, PostureRules.Percent(0, 0));
    }

    [Fact]
    public void Notifications_cover_each_event_and_dedupe_by_key()
    {
        var testId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var facts = new NotificationFacts(
            [new FailingTestFact(testId, "MFA enforced", "AC-07", "Priya.Nair@Example.com", "2 users without MFA")],
            [
                new TaskFact(taskId, "Rotate keys", "lena@example.com", Today.AddDays(2), "Open"),
                new TaskFact(taskId, "Rotate keys", "lena@example.com", Today.AddDays(2), "Open"),
                new TaskFact(Guid.NewGuid(), "Done task", "lena@example.com", Today, "Done"),
            ],
            [
                new AckFact(versionId, "POL-ISP", "Information security policy", "dev@example.com", Today, false),
                new AckFact(Guid.NewGuid(), "POL-ACC", "Access control policy", "dev@example.com", Today, true),
            ],
            [
                new ReviewFact("Risk", "R-1", "R-1 Data loss", "marcus@example.com", Today.AddDays(5), "/risks"),
                new ReviewFact("Risk", "R-2", "R-2 Far away", "marcus@example.com", Today.AddDays(40), "/risks"),
            ],
            [new RequestFact(requestId, "REQ-1", "Access list", Today.AddDays(4), true), new RequestFact(Guid.NewGuid(), "REQ-2", "Closed", Today, false)],
            [new VendorFact(Guid.NewGuid(), "Acme", Today.AddDays(-1)), new VendorFact(Guid.NewGuid(), "Fresh", Today.AddDays(10))]);

        var drafts = NotificationRules.Generate(facts, Today);

        Assert.Equal(6, drafts.Count);
        Assert.Equal(NotificationKinds.All.Order(), drafts.Select(item => item.Kind).Order());
        var failing = drafts.Single(item => item.Kind == NotificationKinds.TestFailing);
        Assert.Equal("priya.nair@example.com", failing.RecipientEmail);
        Assert.Equal("/controls/AC-07", failing.Href);
        Assert.Contains("2026-11-14", failing.DedupeKey, StringComparison.Ordinal);
        Assert.Null(drafts.Single(item => item.Kind == NotificationKinds.AuditorRequest).RecipientEmail);
        Assert.Contains("Acme", drafts.Single(item => item.Kind == NotificationKinds.VendorOverdue).Title, StringComparison.Ordinal);
        Assert.Equal("dev@example.com", drafts.Single(item => item.Kind == NotificationKinds.PolicyAck).RecipientEmail);
        Assert.Equal(drafts.Count, drafts.Select(item => item.DedupeKey).Distinct().Count());
    }

    [Fact]
    public void Broadcast_notifications_are_hidden_from_auditors_and_direct_ones_from_others()
    {
        Assert.True(NotificationRules.Visible(null, "a@example.com", readerIsAuditor: false));
        Assert.False(NotificationRules.Visible(null, "auditor@example.com", readerIsAuditor: true));
        Assert.True(NotificationRules.Visible("a@example.com", " A@Example.com ", readerIsAuditor: false));
        Assert.False(NotificationRules.Visible("a@example.com", "b@example.com", readerIsAuditor: false));
    }

    [Fact]
    public void Search_respects_role_and_auditor_framework_scope()
    {
        SearchEntry[] entries =
        [
            new("control", "AC-07", "Multi-factor authentication", "Passing", "/controls/AC-07", ["soc2"]),
            new("control", "PP-01", "Privacy notice", "Passing", "/controls/PP-01", ["gdpr"]),
            new("policy", "POL-ACC", "Access control policy", "Owner", "/policies", ["soc2"]),
            new("risk", "R-1", "Access leak", "Owner", "/risks", []),
            new("person", "dev@example.com", "Dev Access", "Engineering", "/people", []),
            new("framework", "soc2", "SOC 2", "2017", "/frameworks/soc2", ["soc2"]),
        ];

        var admin = SearchRules.Filter(entries, "access", TenantRole.Admin, []);
        Assert.Equal(3, admin.Count);

        var auditor = SearchRules.Filter(entries, "a", TenantRole.Auditor, ["soc2"]);
        Assert.All(auditor, item => Assert.Contains("soc2", item.FrameworkKeys));
        Assert.DoesNotContain(auditor, item => item.Kind is "risk" or "person");
        Assert.DoesNotContain(auditor, item => item.Key == "PP-01");

        var employee = SearchRules.Filter(entries, "access", TenantRole.Employee, []);
        Assert.Equal(["policy"], employee.Select(item => item.Kind).Distinct());

        Assert.Equal("AC-07", SearchRules.Filter(entries, "ac-07", TenantRole.Admin, [])[0].Key);
        Assert.Empty(SearchRules.Filter(entries, "  ", TenantRole.Admin, []));
    }

    [Fact]
    public void Local_reset_token_expires_and_breaks_after_password_change()
    {
        var token = LocalResetTokens.Create("key-1", "Dev@Example.com", "hash-a", Now.AddMinutes(30));

        Assert.Equal("dev@example.com", LocalResetTokens.ReadEmail(token));
        Assert.True(LocalResetTokens.Validate("key-1", token, "dev@example.com", "hash-a", Now));
        Assert.False(LocalResetTokens.Validate("key-1", token, "dev@example.com", "hash-a", Now.AddMinutes(31)));
        Assert.False(LocalResetTokens.Validate("key-1", token, "dev@example.com", "hash-b", Now));
        Assert.False(LocalResetTokens.Validate("key-2", token, "dev@example.com", "hash-a", Now));
        Assert.False(LocalResetTokens.Validate("key-1", token + "x", "dev@example.com", "hash-a", Now));
        Assert.Null(LocalResetTokens.ReadEmail("not-a-token"));
    }
}
