using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class CloudSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(27017, "0.0.0.0/0", "MongoDB", "Critical", 98)]
    [InlineData(22, "0.0.0.0/0", "SSH", "Critical", 92)]
    [InlineData(3389, "Internet", "RDP", "Critical", 94)]
    [InlineData(5432, "::/0", "PostgreSQL", "Critical", 96)]
    [InlineData(443, "0.0.0.0/0", "HTTPS", "Low", 32)]
    [InlineData(22, "10.0.0.0/8", "SSH", "Medium", 57)]
    public void ExposureAnalyzer_evaluates_ports_and_cidrs(int port, string cidr, string service, string severity, int score)
    {
        var result = ExposureAnalyzer.EvaluatePort(port, cidr);
        Assert.Equal(service, result.Service);
        Assert.Equal(severity, result.Severity);
        Assert.Equal(score, result.RiskScore);
    }

    [Fact]
    public void ExposureAnalyzer_scans_multi_cloud_snapshots_and_generates_iac_patches()
    {
        var snapshots = new NormalizedResource[]
        {
            new("aws", "bucket", "b1", "public-exports", new Dictionary<string, string> { ["public"] = "true" }),
            new("aws", "bucket", "b2", "private-archive", new Dictionary<string, string> { ["public"] = "false" }),
            new("azure", "network", "n1", "eu-nsg", new Dictionary<string, string> { ["open"] = "true", ["port"] = "3389", ["cidr"] = "0.0.0.0/0" }),
            new("gcp", "iam", "i1", "project-iam", new Dictionary<string, string> { ["public"] = "true" }),
        };

        var findings = ExposureAnalyzer.AnalyzeSnapshots(snapshots, Now);

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, item => item.ResourceName == "public-exports" && item.RemediationTerraform.Contains("aws_s3_bucket_public_access_block", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.ResourceName == "eu-nsg" && item.Port == 3389 && item.RemediationCli.Contains("az network nsg", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.ResourceName == "project-iam" && item.ControlCode == "AC-07");
    }

    [Fact]
    public void DspmScorer_ranks_unencrypted_public_pii_highest()
    {
        var critical = DspmScorer.Score("Restricted", publicAccess: true, encrypted: false, "PII,SSN", retentionCompliant: false);
        var safe = DspmScorer.Score("Internal", publicAccess: false, encrypted: true, "Operational", retentionCompliant: true);

        Assert.Equal(100, critical);
        Assert.Equal("Critical", DspmScorer.Level(critical));
        Assert.True(safe < 35);
        Assert.Equal("Low", DspmScorer.Level(safe));
    }

    [Fact]
    public void FinOpsAnalyzer_separates_open_realized_and_compliance_linked_savings()
    {
        var rows = new CostRecommendation[]
        {
            new() { Status = "Open", MonthlySavingsUsd = 500m, LinkedControlCode = "CR-02" },
            new() { Status = "Open", MonthlySavingsUsd = 200m, LinkedControlCode = "" },
            new() { Status = "Applied", MonthlySavingsUsd = 300m, LinkedControlCode = "CFG-01" },
            new() { Status = "Dismissed", MonthlySavingsUsd = 999m, LinkedControlCode = "CFG-01" },
        };

        var summary = FinOpsAnalyzer.Summarize(rows);

        Assert.Equal(700m, summary.OpenMonthlyUsd);
        Assert.Equal(8400m, summary.OpenAnnualUsd);
        Assert.Equal(300m, summary.RealizedMonthlyUsd);
        Assert.Equal(3600m, summary.RealizedAnnualUsd);
        Assert.Equal(1, summary.ComplianceLinkedCount);
        Assert.Equal(500m, summary.ComplianceLinkedMonthlyUsd);
    }

    [Fact]
    public void SarifParser_parses_secret_license_and_cve_results_and_redacts_tokens()
    {
        const string sarif = """
        {
          "version": "2.1.0",
          "runs": [
            {
              "tool": { "driver": { "name": "Trivy" } },
              "results": [
                {
                  "ruleId": "GITLEAKS-SECRET-TOKEN",
                  "level": "error",
                  "message": { "text": "Hardcoded secret token detected in config" },
                  "locations": [
                    {
                      "physicalLocation": {
                        "artifactLocation": { "uri": "deploy/app.env" },
                        "region": {
                          "startLine": 17,
                          "snippet": { "text": "SERVICE_TOKEN=abc1234567890defghijk" }
                        }
                      }
                    }
                  ]
                },
                {
                  "ruleId": "LICENSE-AGPL-3.0",
                  "level": "warning",
                  "message": { "text": "AGPL-3.0 copyleft package found" }
                },
                {
                  "ruleId": "CVE-2024-99999",
                  "level": "error",
                  "message": { "text": "OpenSSL heap vulnerability in container image" }
                }
              ]
            }
          ]
        }
        """;

        var findings = SarifParser.Parse(sarif, "worker-image:1.0", Now);

        Assert.Equal(3, findings.Count);
        var secret = Assert.Single(findings, item => item.Kind == "Secret");
        Assert.Equal("Critical", secret.Severity);
        Assert.Equal("deploy/app.env", secret.Location);
        Assert.Equal(17, secret.LineNumber);
        Assert.Contains("[REDACTED]", secret.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("abc1234567890defghijk", secret.Snippet, StringComparison.Ordinal);

        Assert.Equal("CM-11", Assert.Single(findings, item => item.Kind == "License").ControlCode);
        Assert.Equal("VM-01", Assert.Single(findings, item => item.Kind == "ContainerCve").ControlCode);
    }

    [Fact]
    public void SarifParser_handles_empty_or_empty_runs_gracefully()
    {
        Assert.Empty(SarifParser.Parse("", "asset", Now));
        Assert.Empty(SarifParser.Parse("{}", "asset", Now));
    }

    [Fact]
    public void AttackPathAnalyzer_correlates_exposure_secret_identity_and_crown_jewel_store()
    {
        var exposures = new ExposureFinding[]
        {
            new()
            {
                Provider = "aws",
                ResourceName = "staging-exports",
                ResourceKind = "StorageBucket",
                Cidr = "0.0.0.0/0",
                Port = 443,
                Service = "Public Object Storage",
                Severity = "Critical",
                RiskScore = 91,
                ControlCode = "CFG-01",
                Status = "Open",
                Summary = "Public bucket",
            },
        };
        var scans = new SecurityScanFinding[]
        {
            new()
            {
                Kind = "Secret",
                Severity = "Critical",
                AssetName = "staging-worker",
                Location = "app/.env",
                LineNumber = 12,
                Title = "Cloud access key",
                Source = "Trivy",
                ControlCode = "CR-02",
                Status = "Open",
            },
        };
        var people = new DirectoryPerson[]
        {
            new() { Name = "Dev Patel", Email = "dev@company.example", State = PersonState.Active, MfaEnrolled = false },
        };
        var stores = new DataStoreAsset[]
        {
            new()
            {
                StoreName = "staging-exports",
                Provider = "aws",
                Engine = "AWS S3",
                Region = "us-east-1",
                Classification = "Restricted",
                SensitiveDataTypes = "PII,SSN",
                RecordCountEstimate = 100_000,
                Encrypted = false,
                PublicAccess = true,
                ControlCode = "CFG-01",
                RiskScore = 98,
            },
        };

        var paths = AttackPathAnalyzer.Build(exposures, [], scans, people, stores);

        Assert.NotEmpty(paths);
        var top = paths[0];
        Assert.True(top.BlastRadiusScore >= 80);
        Assert.Equal("Critical", top.Severity);
        Assert.Equal(["Entry", "Credential", "Identity", "Crown Jewel"], top.Hops.Select(hop => hop.Stage).ToArray());
        Assert.Contains("CFG-01", top.ControlCodes);
        Assert.Contains("CR-02", top.ControlCodes);
        Assert.Contains("AC-07", top.ControlCodes);
    }
}
