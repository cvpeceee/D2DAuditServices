using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class ConnectorCatalogTests
{
    private static readonly string[] HarborlineFailing = ["endpoint-edr", "training-completed", "vulnerability-sla"];

    [Fact]
    public void Catalog_has_at_least_45_new_connectors_with_unique_keys_and_fixtures()
    {
        var connectors = ConnectorCatalog.Connectors;
        var added = connectors.Where(item => !item.Builtin).ToArray();

        Assert.True(added.Length >= 45, $"Only {added.Length} new connectors.");
        Assert.Equal(connectors.Count, connectors.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(added, item =>
        {
            Assert.Contains(item.Category, ConnectorCatalog.Categories);
            Assert.NotEmpty(item.Fields);
            Assert.NotEmpty(item.Tests);
            Assert.NotEmpty(ConnectorCatalog.Fixtures(item.Key));
            Assert.All(ConnectorCatalog.Fixtures(item.Key), resource => Assert.Equal(item.Key, resource.Provider));
            Assert.StartsWith("https://", item.DocsUrl, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Catalog_check_metadata_matches_the_automated_check_specs()
    {
        foreach (var check in ConnectorCatalog.Checks)
        {
            var spec = Assert.Single(AutomatedChecks.All, item => item.Key == check.Key);
            Assert.Equal(spec.Name, check.Name);
            Assert.Equal(spec.ControlCode, check.ControlCode);
        }

        var known = AutomatedChecks.All.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(ConnectorCatalog.Connectors.SelectMany(item => item.Tests), test => Assert.Contains(test, known));
    }

    [Fact]
    public async Task Every_new_connector_feeds_at_least_one_of_its_tests_from_mock_fixtures()
    {
        foreach (var descriptor in ConnectorCatalog.Connectors.Where(item => !item.Builtin))
        {
            var collected = await new MockConnector(descriptor).CollectAsync(new Dictionary<string, string>(), CancellationToken.None);
            Assert.True(collected.IsSuccess);
            var outcomes = descriptor.Tests.Select(test => AutomatedChecks.Evaluate(test, collected.Value!).Outcome).ToArray();
            Assert.True(outcomes.Any(outcome => outcome != TestOutcome.NotRun), descriptor.Key + " produced no test outcome.");
        }
    }

    [Fact]
    public void Harborline_connected_set_fails_only_the_expected_new_checks()
    {
        var resources = ConnectorCatalog.Connectors
            .Where(item => item.Harborline && !item.Builtin)
            .SelectMany(item => ConnectorCatalog.Fixtures(item.Key))
            .ToArray();

        var failing = ConnectorCatalog.Checks
            .Where(check => AutomatedChecks.Evaluate(check.Key, resources).Outcome == TestOutcome.Fail)
            .Select(check => check.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var passing = ConnectorCatalog.Checks.Count(check => AutomatedChecks.Evaluate(check.Key, resources).Outcome == TestOutcome.Pass);

        Assert.Equal(HarborlineFailing, failing);
        Assert.True(passing >= 10, $"Only {passing} checks passed.");
    }

    [Fact]
    public void Credentials_merge_keeps_stored_secrets_and_redaction_never_returns_them()
    {
        var descriptor = ConnectorCatalog.Find("okta")!;
        var stored = new Dictionary<string, string> { ["apiToken"] = "00stored-token", ["domain"] = "old.okta.com" };
        var submitted = new Dictionary<string, string> { ["apiToken"] = " ", ["domain"] = " acme.okta.com ", ["UNKNOWN"] = "x" };

        var merged = ConnectorCredentials.Merge(descriptor, stored, submitted);
        var redacted = ConnectorCredentials.Redact(descriptor, merged);

        Assert.Equal("00stored-token", merged["apiToken"]);
        Assert.Equal("acme.okta.com", merged["domain"]);
        Assert.False(merged.ContainsKey("UNKNOWN"));
        var token = Assert.Single(redacted, item => item.Key == "apiToken");
        Assert.True(token.Configured);
        Assert.Null(token.Value);
        Assert.Equal("acme.okta.com", Assert.Single(redacted, item => item.Key == "domain").Value);
        Assert.Equal("upstream said [redacted]", ConnectorCredentials.Scrub(descriptor, merged, "upstream said 00stored-token"));
    }

    [Fact]
    public void Mode_is_live_only_with_complete_config_outside_example_workspaces()
    {
        var descriptor = ConnectorCatalog.Find("okta")!;
        var complete = new Dictionary<string, string> { ["apiToken"] = "t", ["domain"] = "acme.okta.com" };
        var partial = new Dictionary<string, string> { ["domain"] = "acme.okta.com" };

        Assert.Equal(ConnectorModes.Live, ConnectorCredentials.Mode(descriptor, complete, example: false));
        Assert.Equal(ConnectorModes.Mock, ConnectorCredentials.Mode(descriptor, complete, example: true));
        Assert.Equal(ConnectorModes.Mock, ConnectorCredentials.Mode(descriptor, partial, example: false));
        Assert.Equal(["apiToken"], ConnectorCredentials.Missing(descriptor, partial));
    }
}
