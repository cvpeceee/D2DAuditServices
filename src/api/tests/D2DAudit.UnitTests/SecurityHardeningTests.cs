using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class SecurityHardeningTests
{
    [Theory]
    [InlineData("https://checkr.example/reports/123", true)]
    [InlineData("http://localhost:5173/integrations", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JAVASCRIPT:alert(document.cookie)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("//evil.example/phish", false)]
    [InlineData("https://example.com/\r\nSet-Cookie:a=b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeUrl_accepts_only_http_and_https_without_control_characters(string? url, bool expected)
    {
        Assert.Equal(expected, SafeUrl.IsHttpOrHttps(url));
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    [InlineData("+SUM(A1:A2)", "\"'+SUM(A1:A2)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(1+1)", "\"'@SUM(1+1)\"")]
    [InlineData("\tformula", "\"'\tformula\"")]
    [InlineData("normal \"quoted\" text", "\"normal \"\"quoted\"\" text\"")]
    [InlineData("AC-07", "\"AC-07\"")]
    [InlineData("", "\"\"")]
    public void CsvSafety_neutralizes_spreadsheet_formulas_and_escapes_quotes(string input, string expected)
    {
        Assert.Equal(expected, CsvSafety.EscapeCell(input));
    }

    [Fact]
    public void TaskRecurrence_calculates_next_due_dates()
    {
        var start = new DateOnly(2026, 1, 15);
        Assert.Equal(new DateOnly(2026, 1, 22), TaskRecurrence.NextDue(start, "Weekly"));
        Assert.Equal(new DateOnly(2026, 2, 15), TaskRecurrence.NextDue(start, "Monthly"));
        Assert.Equal(new DateOnly(2026, 4, 15), TaskRecurrence.NextDue(start, "Quarterly"));
        Assert.Equal(new DateOnly(2027, 1, 15), TaskRecurrence.NextDue(start, "Annual"));
        Assert.Null(TaskRecurrence.NextDue(start, "None"));
        Assert.Null(TaskRecurrence.NextDue(start, null));
    }

    [Fact]
    public void RedirectPolicy_blocks_unlisted_origins_and_javascript_schemes()
    {
        var allowed = new[] { "http://localhost:5173", "https://app.d2daudit.example/auth/callback" };
        Assert.True(RedirectPolicy.IsAllowed("http://localhost:5173/integrations", allowed));
        Assert.True(RedirectPolicy.IsAllowed("https://app.d2daudit.example/auth/callback", allowed));
        Assert.True(RedirectPolicy.IsAllowed("", allowed));
        Assert.False(RedirectPolicy.IsAllowed("https://evil.example/steal", allowed));
        Assert.False(RedirectPolicy.IsAllowed("https://app.d2daudit.example/other", allowed));
        Assert.False(RedirectPolicy.IsAllowed("javascript:alert(1)", allowed));
        // Must fail closed when no origins are configured.
        Assert.False(RedirectPolicy.IsAllowed("https://evil.example/steal", []));
    }

    [Theory]
    [InlineData("https://hooks.customer.example/events", true)]
    [InlineData("http://siem.partner.org:8443/d2d", true)]
    [InlineData("http://localhost:8080/hook", false)]
    [InlineData("http://127.0.0.1/hook", false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://10.0.0.15/internal", false)]
    [InlineData("http://172.16.4.2/internal", false)]
    [InlineData("http://192.168.1.10/internal", false)]
    [InlineData("http://[::1]/hook", false)]
    [InlineData("https://metadata.internal/hook", false)]
    [InlineData("https://service.local/hook", false)]
    [InlineData("https://user:pass@hooks.customer.example/events", false)]
    [InlineData("javascript:alert(1)", false)]
    public void SafeUrl_IsPublicHttpOrHttps_blocks_ssrf_targets(string url, bool expected)
    {
        Assert.Equal(expected, SafeUrl.IsPublicHttpOrHttps(url));
    }

    [Theory]
    [InlineData("https://api.github.com/orgs/acme/repos?page=2", "https://api.github.com/orgs/acme/repos", true)]
    [InlineData("https://evil.example/steal?page=2", "https://api.github.com/orgs/acme/repos", false)]
    [InlineData("http://api.github.com/orgs/acme/repos?page=2", "https://api.github.com/orgs/acme/repos", false)]
    [InlineData("https://api.github.com:8443/orgs/acme/repos", "https://api.github.com/orgs/acme/repos", false)]
    public void SafeUrl_IsSameOrigin_prevents_pagination_token_exfiltration(string candidate, string origin, bool expected)
    {
        Assert.Equal(expected, SafeUrl.IsSameOrigin(candidate, origin));
    }

    [Fact]
    public void ExposureAnalyzer_escapes_malicious_resource_names_in_hcl_and_cli()
    {
        var hostile = new NormalizedResource(
            "aws",
            "network",
            "sg-hostile",
            "sg-\"; rm -rf /; ${secret}",
            new Dictionary<string, string>
            {
                ["open"] = "true",
                ["port"] = "22",
                ["cidr"] = "0.0.0.0/0",
                ["region"] = "us-east-1",
            });
        var findings = ExposureAnalyzer.AnalyzeSnapshots([hostile], DateTimeOffset.UtcNow);
        var finding = Assert.Single(findings);
        Assert.DoesNotContain("\"sg-\";", finding.RemediationTerraform, StringComparison.Ordinal);
        Assert.Contains("\"sg-\\\"; rm -rf /; $${secret}\"", finding.RemediationTerraform, StringComparison.Ordinal);
        Assert.Contains("'sg-\"; rm -rf /; ${secret}'", finding.RemediationCli, StringComparison.Ordinal);
    }

    [Fact]
    public void SarifParser_redacts_pem_blocks_alpha_tokens_and_secret_messages()
    {
        var sarif = """
            {
              "runs": [{
                "tool": { "driver": { "name": "gitleaks" } },
                "results": [{
                  "ruleId": "aws-secret-key",
                  "level": "error",
                  "message": { "text": "Leaked token ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456 and password=SuperSecretValue123" },
                  "locations": [{
                    "physicalLocation": {
                      "artifactLocation": { "uri": "config/prod.env" },
                      "region": {
                        "startLine": 12,
                        "snippet": { "text": "-----BEGIN PRIVATE KEY-----\nMIIEvAIBADANBgkqhkiG9w0\n-----END PRIVATE KEY-----" }
                      }
                    }
                  }]
                }]
              }]
            }
            """;
        var findings = SarifParser.Parse(sarif, "worker:1.0", DateTimeOffset.UtcNow);
        var secret = Assert.Single(findings);
        Assert.Equal("Secret", secret.Kind);
        Assert.DoesNotContain("ghp_ABCDEF", secret.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecretValue123", secret.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("MIIEvAIBADAN", secret.Snippet, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PRIVATE-KEY]", secret.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomFrameworkCsv_preserves_substrings_containing_shall()
    {
        var csv = "code,title,summary\nCUST-01,Marshall Data,\"Marshall handlers shall enforce TLS 1.3.\"";
        var rows = CustomFrameworkCsv.Parse(csv);
        var row = Assert.Single(rows);
        Assert.Contains("Marshall handlers must enforce TLS 1.3.", row.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Marmust", row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustReceiptBuilder_reports_broken_chain_and_escapes_pipe_delimiters()
    {
        var receipt = TrustReceiptBuilder.Build(
            "acme",
            "Acme | Corp\nInc",
            "US",
            6,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            [new TrustReceiptFramework("soc2", "SOC 2", 40, 42, 95)],
            chainEntryCount: 5,
            latestSequence: 5,
            chainHeadHash: "deadbeef",
            chainVerified: false);
        Assert.Equal("Broken", receipt.ChainStatus);
        Assert.DoesNotContain("Acme | Corp", receipt.CanonicalPayload, StringComparison.Ordinal);
        Assert.True(TrustReceiptBuilder.Verify(receipt.CanonicalPayload, receipt.ReceiptSha256));
    }

    [Fact]
    public void ApiKeyTokens_and_WebhookSigning_round_trip_and_enforce_scopes()
    {
        var tenantId = Guid.NewGuid();
        var plaintext = ApiKeyTokens.Create(tenantId);
        Assert.True(ApiKeyTokens.IsApiKey(plaintext));
        Assert.True(ApiKeyTokens.TryParseTenantId(plaintext, out var parsed));
        Assert.Equal(tenantId, parsed);
        Assert.StartsWith("d2d_api_", plaintext, StringComparison.Ordinal);
        Assert.False(ApiKeyTokens.CanWrite("read:controls,read:evidence"));
        Assert.True(ApiKeyTokens.CanWrite("read:controls,write:evidence"));

        var secret = "whsec_test_secret_1234567890";
        var payload = "{\"event\":\"test.failed\"}";
        var ts = 1_760_000_000L;
        var sig = WebhookSigning.Sign(secret, ts, payload);
        Assert.True(WebhookSigning.Verify(secret, ts, payload, sig));
        Assert.False(WebhookSigning.Verify(secret, ts, payload + "x", sig));
        Assert.False(WebhookSigning.Verify("whsec_wrong", ts, payload, sig));
        Assert.True(WebhookSigning.MatchesEvent("test.failed,exposure.*", "exposure.critical"));
        Assert.False(WebhookSigning.MatchesEvent("test.failed", "exposure.critical"));
        Assert.Equal("exposure.critical", WebhookSigning.EventNameForTrigger("ExposureCritical"));
    }
}
