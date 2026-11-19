using System.Globalization;
using System.Text.Json;

namespace D2DAudit.Domain;

public sealed class ExposureFinding
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = "";

    public string ResourceName { get; set; } = "";

    public string ResourceKind { get; set; } = "";

    public string Region { get; set; } = "";

    public string Cidr { get; set; } = "";

    public int Port { get; set; }

    public string Protocol { get; set; } = "tcp";

    public string Service { get; set; } = "";

    public string Severity { get; set; } = "High";

    public int RiskScore { get; set; }

    public string ControlCode { get; set; } = "CFG-01";

    public string Summary { get; set; } = "";

    public string RemediationTerraform { get; set; } = "";

    public string RemediationCli { get; set; } = "";

    public string Status { get; set; } = "Open";

    public string ResolutionNote { get; set; } = "";

    public DateTimeOffset DetectedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class SecurityScanFinding
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = "ContainerCve";

    public string Severity { get; set; } = "High";

    public string AssetName { get; set; } = "";

    public string Location { get; set; } = "";

    public int LineNumber { get; set; }

    public string RuleId { get; set; } = "";

    public string Title { get; set; } = "";

    public string Detail { get; set; } = "";

    public string Snippet { get; set; } = "";

    public string Source { get; set; } = "";

    public string FixVersion { get; set; } = "";

    public string ControlCode { get; set; } = "VM-01";

    public string Status { get; set; } = "Open";

    public DateTimeOffset DetectedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class DataStoreAsset
{
    public Guid Id { get; set; }

    public string StoreName { get; set; } = "";

    public string Provider { get; set; } = "";

    public string Engine { get; set; } = "";

    public string Region { get; set; } = "";

    public string Classification { get; set; } = "Confidential";

    public string SensitiveDataTypes { get; set; } = "";

    public long RecordCountEstimate { get; set; }

    public bool Encrypted { get; set; }

    public bool PublicAccess { get; set; }

    public bool RetentionCompliant { get; set; }

    public string ControlCode { get; set; } = "CR-02";

    public int RiskScore { get; set; }

    public string OwnerEmail { get; set; } = "";

    public DateTimeOffset LastScannedAt { get; set; }
}

public sealed class CostRecommendation
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = "";

    public string ResourceName { get; set; } = "";

    public string ResourceType { get; set; } = "";

    public string Region { get; set; } = "";

    public string Category { get; set; } = "";

    public decimal MonthlySavingsUsd { get; set; }

    public decimal AnnualSavingsUsd { get; set; }

    public string LinkedControlCode { get; set; } = "";

    public string ComplianceImpact { get; set; } = "";

    public string RemediationSummary { get; set; } = "";

    public string Status { get; set; } = "Open";

    public DateTimeOffset DetectedAt { get; set; }

    public DateTimeOffset? ActionedAt { get; set; }
}

public sealed record FinOpsSummary(
    decimal OpenMonthlyUsd,
    decimal OpenAnnualUsd,
    decimal RealizedMonthlyUsd,
    decimal RealizedAnnualUsd,
    int ComplianceLinkedCount,
    decimal ComplianceLinkedMonthlyUsd);

public sealed record AttackHop(
    string Stage,
    string Kind,
    string Label,
    string Detail,
    string Href,
    string Severity);

public sealed record AttackPath(
    string Id,
    string Title,
    int BlastRadiusScore,
    string Severity,
    IReadOnlyList<AttackHop> Hops,
    IReadOnlyList<string> ControlCodes,
    string ChokepointRemediation);

public static class ExposureAnalyzer
{
    public static (string Service, string Severity, int RiskScore) EvaluatePort(int port, string cidr)
    {
        var internetWide = cidr is "0.0.0.0/0" or "::/0" or "*" or "Internet";
        var baseScore = port switch
        {
            22 => ("SSH", "Critical", 92),
            3389 => ("RDP", "Critical", 94),
            27017 => ("MongoDB", "Critical", 98),
            5432 => ("PostgreSQL", "Critical", 96),
            3306 => ("MySQL", "Critical", 96),
            6379 => ("Redis", "Critical", 97),
            9200 => ("Elasticsearch", "Critical", 95),
            1433 => ("SQL Server", "Critical", 95),
            2375 => ("Docker API", "Critical", 99),
            80 => ("HTTP", "Medium", 58),
            8080 => ("HTTP-Alt", "High", 74),
            443 => ("HTTPS", "Low", 32),
            0 => ("All Ports", "Critical", 96),
            _ => ("TCP-" + port.ToString(CultureInfo.InvariantCulture), "High", 72),
        };

        if (internetWide)
        {
            return baseScore;
        }

        var reduced = Math.Max(20, baseScore.Item3 - 35);
        var sev = reduced switch
        {
            >= 75 => "High",
            >= 45 => "Medium",
            _ => "Low",
        };
        return (baseScore.Item1, sev, reduced);
    }

    public static IReadOnlyList<ExposureFinding> AnalyzeSnapshots(
        IReadOnlyList<NormalizedResource> snapshots,
        DateTimeOffset now)
    {
        var findings = new List<ExposureFinding>();
        foreach (var resource in snapshots)
        {
            if (resource.Kind == "bucket" && Flag(resource, "public") == "true")
            {
                findings.Add(new ExposureFinding
                {
                    Id = Guid.NewGuid(),
                    Provider = resource.Provider,
                    ResourceName = resource.Name,
                    ResourceKind = "StorageBucket",
                    Region = RegionOf(resource),
                    Cidr = "0.0.0.0/0",
                    Port = 443,
                    Protocol = "https",
                    Service = "Public Object Storage",
                    Severity = "Critical",
                    RiskScore = 91,
                    ControlCode = "CFG-01",
                    Summary = $"Bucket {resource.Name} allows public internet reads or ACL listing.",
                    RemediationTerraform = BucketTerraform(resource.Provider, resource.Name),
                    RemediationCli = BucketCli(resource.Provider, resource.Name),
                    Status = "Open",
                    DetectedAt = now,
                });
            }
            else if (resource.Kind == "network" && Flag(resource, "open") == "true")
            {
                var port = int.TryParse(Flag(resource, "port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 22;
                var cidr = string.IsNullOrWhiteSpace(Flag(resource, "cidr")) ? "0.0.0.0/0" : Flag(resource, "cidr");
                var (service, severity, score) = EvaluatePort(port, cidr);
                findings.Add(new ExposureFinding
                {
                    Id = Guid.NewGuid(),
                    Provider = resource.Provider,
                    ResourceName = resource.Name,
                    ResourceKind = "NetworkFirewall",
                    Region = RegionOf(resource),
                    Cidr = cidr,
                    Port = port,
                    Protocol = "tcp",
                    Service = service,
                    Severity = severity,
                    RiskScore = score,
                    ControlCode = "CFG-01",
                    Summary = $"{resource.Provider.ToUpperInvariant()} firewall {resource.Name} allows {cidr} inbound on {service} ({port}).",
                    RemediationTerraform = FirewallTerraform(resource.Provider, resource.Name, port),
                    RemediationCli = FirewallCli(resource.Provider, resource.Name, port, cidr),
                    Status = "Open",
                    DetectedAt = now,
                });
            }
            else if (resource.Kind == "database" && Flag(resource, "public") == "true")
            {
                var port = int.TryParse(Flag(resource, "port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 5432;
                var (service, severity, score) = EvaluatePort(port, "0.0.0.0/0");
                findings.Add(new ExposureFinding
                {
                    Id = Guid.NewGuid(),
                    Provider = resource.Provider,
                    ResourceName = resource.Name,
                    ResourceKind = "ManagedDatabase",
                    Region = RegionOf(resource),
                    Cidr = "0.0.0.0/0",
                    Port = port,
                    Protocol = "tcp",
                    Service = service,
                    Severity = severity,
                    RiskScore = score,
                    ControlCode = "CFG-01",
                    Summary = $"Database {resource.Name} has public accessibility enabled on port {port}.",
                    RemediationTerraform = $"resource \"aws_db_instance\" \"{SafeId(resource.Name)}\" {{\n  publicly_accessible = false\n  storage_encrypted   = true\n}}",
                    RemediationCli = $"aws rds modify-db-instance --db-instance-identifier {ShellArg(resource.Name)} --no-publicly-accessible --apply-immediately",
                    Status = "Open",
                    DetectedAt = now,
                });
            }
            else if (resource.Kind == "iam" && Flag(resource, "public") == "true")
            {
                findings.Add(new ExposureFinding
                {
                    Id = Guid.NewGuid(),
                    Provider = resource.Provider,
                    ResourceName = resource.Name,
                    ResourceKind = "IamPolicy",
                    Region = "global",
                    Cidr = "allUsers",
                    Port = 443,
                    Protocol = "https",
                    Service = "Public IAM Binding",
                    Severity = "Critical",
                    RiskScore = 93,
                    ControlCode = "AC-07",
                    Summary = $"{resource.Provider.ToUpperInvariant()} IAM policy {resource.Name} grants permissions to allUsers or allAuthenticatedUsers.",
                    RemediationTerraform = "# Remove allUsers and allAuthenticatedUsers members from google_project_iam_binding\nresource \"google_project_iam_binding\" \"restricted\" {\n  members = [\"group:security-ops@company.example\"]\n}",
                    RemediationCli = "gcloud projects remove-iam-policy-binding PROJECT_ID --member=allUsers --role=roles/viewer",
                    Status = "Open",
                    DetectedAt = now,
                });
            }
        }

        return findings.OrderByDescending(item => item.RiskScore).ThenBy(item => item.ResourceName, StringComparer.Ordinal).ToArray();
    }

    private static string RegionOf(NormalizedResource resource)
    {
        var region = Flag(resource, "region");
        if (!string.IsNullOrWhiteSpace(region))
        {
            return region;
        }

        return resource.Provider switch
        {
            "azure" => "eastus",
            "gcp" => "us-central1",
            _ => "us-east-1",
        };
    }

    private static string BucketTerraform(string provider, string name)
    {
        var safeName = HclString(name);
        return provider switch
        {
            "azure" => $"resource \"azurerm_storage_account\" \"{SafeId(name)}\" {{\n  name                     = \"{safeName}\"\n  allow_nested_items_to_be_public = false\n}}",
            "gcp" => $"resource \"google_storage_bucket\" \"{SafeId(name)}\" {{\n  name                     = \"{safeName}\"\n  public_access_prevention = \"enforced\"\n}}",
            _ => $"resource \"aws_s3_bucket_public_access_block\" \"{SafeId(name)}\" {{\n  bucket                  = \"{safeName}\"\n  block_public_acls       = true\n  block_public_policy     = true\n  ignore_public_acls      = true\n  restrict_public_buckets = true\n}}",
        };
    }

    private static string BucketCli(string provider, string name)
    {
        var safeArg = ShellArg(name);
        return provider switch
        {
            "azure" => $"az storage account update --name {safeArg} --allow-blob-public-access false",
            "gcp" => $"gcloud storage buckets update {ShellArg("gs://" + name)} --pap=enforced",
            _ => $"aws s3api put-public-access-block --bucket {safeArg} --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true",
        };
    }

    private static string FirewallTerraform(string provider, string name, int port)
    {
        var safeName = HclString(name);
        var portText = port.ToString(CultureInfo.InvariantCulture);
        return provider switch
        {
            "azure" => $"resource \"azurerm_network_security_rule\" \"deny_{SafeId(name)}_{portText}\" {{\n  name                       = \"restrict-port-{portText}\"\n  direction                  = \"Inbound\"\n  access                     = \"Allow\"\n  source_address_prefix      = \"10.0.0.0/8\"\n  destination_port_range     = \"{portText}\"\n  network_security_group_name = \"{safeName}\"\n}}",
            "gcp" => $"resource \"google_compute_firewall\" \"{SafeId(name)}\" {{\n  name          = \"{safeName}\"\n  source_ranges = [\"10.0.0.0/8\"]\n  allow {{ protocol = \"tcp\" ports = [\"{portText}\"] }}\n}}",
            _ => $"resource \"aws_vpc_security_group_ingress_rule\" \"{SafeId(name)}\" {{\n  security_group_id = \"{safeName}\"\n  cidr_ipv4         = \"10.0.0.0/8\"\n  from_port         = {portText}\n  to_port           = {portText}\n  ip_protocol       = \"tcp\"\n}}",
        };
    }

    private static string FirewallCli(string provider, string name, int port, string cidr)
    {
        var safeName = ShellArg(name);
        var safeCidr = ShellArg(cidr);
        var portText = port.ToString(CultureInfo.InvariantCulture);
        return provider switch
        {
            "azure" => $"az network nsg rule update --nsg-name {safeName} --name AllowInbound{portText} --source-address-prefixes 10.0.0.0/8",
            "gcp" => $"gcloud compute firewall-rules update {safeName} --source-ranges=10.0.0.0/8",
            _ => $"aws ec2 revoke-security-group-ingress --group-name {safeName} --protocol tcp --port {portText} --cidr {safeCidr}",
        };
    }

    private static string HclString(string value)
    {
        var clean = (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("${", "$${", StringComparison.Ordinal);
        return new string(clean.Where(ch => !char.IsControl(ch)).ToArray());
    }

    private static string ShellArg(string value)
    {
        var clean = new string((value ?? "").Where(ch => !char.IsControl(ch)).ToArray());
        if (clean.Length > 0 && clean.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' or ':'))
        {
            return clean;
        }

        return "'" + clean.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string SafeId(string name)
    {
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        return chars.Length == 0 ? "resource" : new string(chars);
    }

    private static string Flag(NormalizedResource resource, string key) =>
        resource.Attributes.TryGetValue(key, out var value) ? value : "";
}

public static class DspmScorer
{
    public static int Score(
        string classification,
        bool publicAccess,
        bool encrypted,
        string sensitiveDataTypes,
        bool retentionCompliant)
    {
        var score = classification.ToUpperInvariant() switch
        {
            "RESTRICTED" => 40,
            "CONFIDENTIAL" => 28,
            "INTERNAL" => 15,
            _ => 5,
        };

        if (publicAccess)
        {
            score += 35;
        }

        if (!encrypted)
        {
            score += 22;
        }

        if (!retentionCompliant)
        {
            score += 10;
        }

        var tags = (sensitiveDataTypes ?? "").ToUpperInvariant();
        if (tags.Contains("SSN", StringComparison.Ordinal)
            || tags.Contains("PHI", StringComparison.Ordinal)
            || tags.Contains("PCI", StringComparison.Ordinal)
            || tags.Contains("CREDITCARD", StringComparison.Ordinal))
        {
            score += 12;
        }
        else if (tags.Contains("PII", StringComparison.Ordinal) || tags.Contains("EMAIL", StringComparison.Ordinal))
        {
            score += 6;
        }

        return Math.Clamp(score, 0, 100);
    }

    public static string Level(int riskScore) => riskScore switch
    {
        >= 80 => "Critical",
        >= 60 => "High",
        >= 35 => "Medium",
        _ => "Low",
    };
}

public static class FinOpsAnalyzer
{
    public static FinOpsSummary Summarize(IEnumerable<CostRecommendation> rows)
    {
        decimal openMonthly = 0;
        decimal realizedMonthly = 0;
        decimal complianceMonthly = 0;
        var complianceCount = 0;
        foreach (var row in rows)
        {
            if (string.Equals(row.Status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                openMonthly += row.MonthlySavingsUsd;
                if (!string.IsNullOrWhiteSpace(row.LinkedControlCode))
                {
                    complianceCount++;
                    complianceMonthly += row.MonthlySavingsUsd;
                }
            }
            else if (string.Equals(row.Status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                realizedMonthly += row.MonthlySavingsUsd;
            }
        }

        return new FinOpsSummary(
            Math.Round(openMonthly, 2),
            Math.Round(openMonthly * 12m, 2),
            Math.Round(realizedMonthly, 2),
            Math.Round(realizedMonthly * 12m, 2),
            complianceCount,
            Math.Round(complianceMonthly, 2));
    }
}

public static class SarifParser
{
    public static IReadOnlyList<SecurityScanFinding> Parse(string sarifJson, string defaultAsset, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sarifJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(sarifJson);
        if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var findings = new List<SecurityScanFinding>();
        foreach (var run in runs.EnumerateArray())
        {
            var toolName = ToolName(run);
            if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                var ruleId = Text(result, "ruleId");
                var message = NestedText(result, "message", "text");
                if (string.IsNullOrWhiteSpace(ruleId) && string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var level = Text(result, "level");
                var (location, line, snippet) = ReadLocation(result);
                var kind = Classify(ruleId, message, toolName);
                var severity = MapSeverity(level, kind);
                var safeMessage = kind == "Secret" ? RedactSnippet(message) : message;
                findings.Add(new SecurityScanFinding
                {
                    Id = Guid.NewGuid(),
                    Kind = kind,
                    Severity = severity,
                    AssetName = string.IsNullOrWhiteSpace(defaultAsset) ? "imported-workload" : defaultAsset.Trim(),
                    Location = location.Length > 300 ? location[..300] : location,
                    LineNumber = Math.Max(0, line),
                    RuleId = TrimMax(string.IsNullOrWhiteSpace(ruleId) ? "SARIF-RULE" : ruleId, 80),
                    Title = TrimMax(string.IsNullOrWhiteSpace(safeMessage) ? ruleId : safeMessage, 300),
                    Detail = TrimMax(safeMessage, 1000),
                    Snippet = TrimMax(RedactSnippet(snippet), 500),
                    Source = TrimMax(toolName, 40),
                    FixVersion = "",
                    ControlCode = kind switch
                    {
                        "Secret" => "CR-02",
                        "License" => "CM-11",
                        _ => "VM-01",
                    },
                    Status = "Open",
                    DetectedAt = now,
                });
            }
        }

        return findings;
    }

    public static string RedactSnippet(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        if (raw.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase))
        {
            return "[REDACTED-PRIVATE-KEY]";
        }

        var tokens = raw.Split([' ', '\t', '\r', '\n'], StringSplitOptions.None);
        var redactNextValue = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 0)
            {
                continue;
            }

            if (redactNextValue)
            {
                tokens[i] = "[REDACTED]";
                redactNextValue = false;
                continue;
            }

            var eq = token.IndexOf('=', StringComparison.Ordinal);
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (eq > 0)
            {
                var keyPart = token[..eq];
                var valPart = token[(eq + 1)..].Trim('"', '\'', ',', ';');
                if (IsSensitiveKey(keyPart) || LooksLikeSecretValue(valPart))
                {
                    tokens[i] = keyPart + "=[REDACTED]";
                    continue;
                }
            }
            else if (colon > 0)
            {
                var keyPart = token[..colon].Trim('"', '\'');
                var valPart = token[(colon + 1)..].Trim('"', '\'', ',', ';');
                if (IsSensitiveKey(keyPart))
                {
                    if (valPart.Length > 0)
                    {
                        tokens[i] = token[..(colon + 1)] + "[REDACTED]";
                    }
                    else
                    {
                        redactNextValue = true;
                    }

                    continue;
                }
            }

            var unquoted = token.Trim('"', '\'', ',', ';', '(', ')', '[', ']', '{', '}');
            if (LooksLikeSecretValue(unquoted))
            {
                tokens[i] = unquoted.Length >= 8 ? unquoted[..4] + "***[REDACTED]" : "[REDACTED]";
            }
        }

        return string.Join(' ', tokens);
    }

    private static bool IsSensitiveKey(string key)
    {
        var upper = key.ToUpperInvariant();
        return upper.Contains("SECRET", StringComparison.Ordinal)
            || upper.Contains("TOKEN", StringComparison.Ordinal)
            || upper.Contains("PASSWORD", StringComparison.Ordinal)
            || upper.Contains("PASSWD", StringComparison.Ordinal)
            || upper.Contains("APIKEY", StringComparison.Ordinal)
            || upper.Contains("API_KEY", StringComparison.Ordinal)
            || upper.Contains("PRIVATE", StringComparison.Ordinal)
            || upper.Contains("CREDENTIAL", StringComparison.Ordinal)
            || upper.Contains("AUTH", StringComparison.Ordinal);
    }

    private static bool LooksLikeSecretValue(string value)
    {
        if (value.Length < 12)
        {
            return false;
        }

        if (value.StartsWith("AKIA", StringComparison.Ordinal)
            || value.StartsWith("ASIA", StringComparison.Ordinal)
            || value.StartsWith("ghp_", StringComparison.Ordinal)
            || value.StartsWith("gho_", StringComparison.Ordinal)
            || value.StartsWith("ghs_", StringComparison.Ordinal)
            || value.StartsWith("sk_live_", StringComparison.Ordinal)
            || value.StartsWith("sk_test_", StringComparison.Ordinal)
            || value.StartsWith("xoxb-", StringComparison.Ordinal)
            || value.StartsWith("xoxp-", StringComparison.Ordinal)
            || value.StartsWith("whsec_", StringComparison.Ordinal)
            || value.StartsWith("d2d_api_", StringComparison.Ordinal)
            || value.StartsWith("eyJ", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Length >= 16 && value.Any(char.IsDigit) && value.Any(char.IsLetter))
        {
            return true;
        }

        // Catch long all-alpha/symbol secrets (e.g. PEM body lines or base64 tokens with no digits)
        return value.Length >= 24 && !value.Contains('/', StringComparison.Ordinal) && value.All(ch => !char.IsWhiteSpace(ch));
    }

    private static string Classify(string ruleId, string message, string tool)
    {
        var combined = (ruleId + " " + message + " " + tool).ToUpperInvariant();
        if (combined.Contains("SECRET", StringComparison.Ordinal)
            || combined.Contains("GITLEAKS", StringComparison.Ordinal)
            || combined.Contains("PRIVATE-KEY", StringComparison.Ordinal)
            || combined.Contains("TOKEN", StringComparison.Ordinal)
            || combined.Contains("CREDENTIAL", StringComparison.Ordinal))
        {
            return "Secret";
        }

        if (combined.Contains("LICENSE", StringComparison.Ordinal)
            || combined.Contains("GPL", StringComparison.Ordinal)
            || combined.Contains("AGPL", StringComparison.Ordinal)
            || combined.Contains("COPYLEFT", StringComparison.Ordinal))
        {
            return "License";
        }

        return "ContainerCve";
    }

    private static string MapSeverity(string level, string kind) => level.ToLowerInvariant() switch
    {
        "error" => kind == "Secret" ? "Critical" : "High",
        "warning" => "Medium",
        "note" => "Low",
        _ => kind == "Secret" ? "Critical" : "Medium",
    };

    private static string ToolName(JsonElement run)
    {
        if (run.TryGetProperty("tool", out var tool)
            && tool.TryGetProperty("driver", out var driver)
            && driver.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? "SARIF";
        }

        return "SARIF";
    }

    private static (string Location, int Line, string Snippet) ReadLocation(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out var locations) || locations.ValueKind != JsonValueKind.Array)
        {
            return ("", 0, "");
        }

        foreach (var loc in locations.EnumerateArray())
        {
            if (!loc.TryGetProperty("physicalLocation", out var physical) || physical.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uri = NestedText(physical, "artifactLocation", "uri");
            var line = 0;
            var snippet = "";
            if (physical.TryGetProperty("region", out var region) && region.ValueKind == JsonValueKind.Object)
            {
                if (region.TryGetProperty("startLine", out var startLine) && startLine.TryGetInt32(out var parsed))
                {
                    line = parsed;
                }

                snippet = NestedText(region, "snippet", "text");
            }

            return (uri, line, snippet);
        }

        return ("", 0, "");
    }

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string NestedText(JsonElement element, string outer, string inner) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(outer, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? Text(nested, inner)
            : "";

    private static string TrimMax(string value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public static class AttackPathAnalyzer
{
    public static IReadOnlyList<AttackPath> Build(
        IReadOnlyList<ExposureFinding> exposures,
        IReadOnlyList<VulnerabilityRecord> vulnerabilities,
        IReadOnlyList<SecurityScanFinding> scans,
        IReadOnlyList<DirectoryPerson> people,
        IReadOnlyList<DataStoreAsset> stores)
    {
        var openExposures = exposures.Where(item => item.Status == "Open").OrderByDescending(item => item.RiskScore).ToArray();
        var openVulns = vulnerabilities.Where(item => !item.Ignored && item.Severity is "Critical" or "High").ToArray();
        var openSecrets = scans.Where(item => item.Status == "Open" && item.Kind == "Secret").ToArray();
        var riskyPeople = people.Where(item =>
            (item.State == PersonState.Active && !item.MfaEnrolled)
            || (item.State == PersonState.Offboarding)).ToArray();
        var sensitiveStores = stores.Where(item => item.RiskScore >= 50 || item.PublicAccess || !item.Encrypted)
            .OrderByDescending(item => item.RiskScore)
            .ToArray();

        var paths = new List<AttackPath>();

        // Path pattern 1: Internet Exposure -> Vulnerable Workload or Baked-in Secret -> Crown Jewel Sensitive Data Store
        foreach (var exposure in openExposures.Take(4))
        {
            var matchedSecret = openSecrets.FirstOrDefault(secret =>
                SharesHint(exposure.ResourceName, secret.AssetName) || SharesHint(exposure.Provider, secret.Detail))
                ?? openSecrets.FirstOrDefault();
            var matchedVuln = openVulns.FirstOrDefault(vuln =>
                SharesHint(exposure.ResourceName, vuln.AssetName))
                ?? openVulns.FirstOrDefault();
            var matchedStore = sensitiveStores.FirstOrDefault(store =>
                string.Equals(store.Provider, exposure.Provider, StringComparison.OrdinalIgnoreCase)
                || SharesHint(exposure.ResourceName, store.StoreName))
                ?? sensitiveStores.FirstOrDefault();

            if (matchedStore is null && matchedSecret is null && matchedVuln is null)
            {
                continue;
            }

            var hops = new List<AttackHop>
            {
                new(
                    "Entry",
                    exposure.ResourceKind,
                    $"{exposure.Provider.ToUpperInvariant()} {exposure.ResourceName} ({exposure.Service}:{exposure.Port})",
                    $"Reachable from {exposure.Cidr}. {exposure.Summary}",
                    "/exposure",
                    exposure.Severity),
            };
            var controls = new HashSet<string>(StringComparer.Ordinal) { exposure.ControlCode };
            var score = Math.Min(50, exposure.RiskScore / 2);

            if (matchedSecret is not null)
            {
                hops.Add(new AttackHop(
                    "Credential",
                    "Baked-in Secret",
                    $"{matchedSecret.Title} ({matchedSecret.Location}:{matchedSecret.LineNumber.ToString(CultureInfo.InvariantCulture)})",
                    $"Exposed in {matchedSecret.AssetName} via {matchedSecret.Source}.",
                    "/vulnerabilities",
                    matchedSecret.Severity));
                controls.Add(matchedSecret.ControlCode);
                score += 22;
            }
            else if (matchedVuln is not null)
            {
                hops.Add(new AttackHop(
                    "Workload",
                    "CVE",
                    $"{matchedVuln.Cve} on {matchedVuln.AssetName}",
                    matchedVuln.Title,
                    "/vulnerabilities",
                    matchedVuln.Severity));
                controls.Add("VM-01");
                score += 18;
            }

            if (riskyPeople.Length > 0)
            {
                var person = riskyPeople[0];
                hops.Add(new AttackHop(
                    "Identity",
                    person.State == PersonState.Offboarding ? "Leaver Access" : "Missing MFA",
                    $"{person.Name} ({person.Email})",
                    person.State == PersonState.Offboarding
                        ? "Offboarding account still has active access in connected providers."
                        : "Active identity lacks MFA enforcement, enabling credential pivot.",
                    "/people",
                    "High"));
                controls.Add(person.State == PersonState.Offboarding ? "HR-08" : "AC-07");
                score += 12;
            }

            if (matchedStore is not null)
            {
                hops.Add(new AttackHop(
                    "Crown Jewel",
                    matchedStore.Engine,
                    $"{matchedStore.StoreName} ({matchedStore.Classification})",
                    $"Contains {matchedStore.SensitiveDataTypes} (~{matchedStore.RecordCountEstimate.ToString("N0", CultureInfo.InvariantCulture)} records) in {matchedStore.Region}.",
                    "/dspm",
                    DspmScorer.Level(matchedStore.RiskScore)));
                controls.Add(matchedStore.ControlCode);
                score += Math.Min(25, matchedStore.RiskScore / 4);
            }

            var finalScore = Math.Clamp(score, 10, 100);
            var title = matchedStore is not null
                ? $"Internet {exposure.Service} ({exposure.ResourceName}) to {matchedStore.StoreName}"
                : $"Internet {exposure.Service} ({exposure.ResourceName}) lateral pivot";
            paths.Add(new AttackPath(
                "AP-" + (paths.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                title,
                finalScore,
                DspmScorer.Level(finalScore),
                hops,
                controls.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
                $"Close {exposure.Cidr} access on {exposure.ResourceName} (port {exposure.Port.ToString(CultureInfo.InvariantCulture)}) first — eliminates entry hop for this entire attack chain."));
        }

        // Path pattern 2: Direct Public Sensitive Data Store (even without separate firewall hop)
        foreach (var store in sensitiveStores.Where(item => item.PublicAccess && !paths.Any(path => path.Title.Contains(item.StoreName, StringComparison.Ordinal))).Take(2))
        {
            var hops = new List<AttackHop>
            {
                new("Entry", "Public Endpoint", $"{store.Provider.ToUpperInvariant()} {store.StoreName}", $"Direct public internet exposure in {store.Region}.", "/dspm", "Critical"),
                new("Crown Jewel", store.Engine, $"{store.SensitiveDataTypes} ({store.Classification})", $"~{store.RecordCountEstimate.ToString("N0", CultureInfo.InvariantCulture)} records; encrypted={store.Encrypted}.", "/dspm", DspmScorer.Level(store.RiskScore)),
            };
            paths.Add(new AttackPath(
                "AP-" + (paths.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                $"Direct public exposure of {store.StoreName} ({store.Classification})",
                Math.Max(85, store.RiskScore),
                "Critical",
                hops,
                ["CFG-01", store.ControlCode],
                $"Enable Block Public Access and default KMS encryption on {store.StoreName}."));
        }

        return paths.OrderByDescending(item => item.BlastRadiusScore).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool SharesHint(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var parts = left.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part.Length >= 4 && right.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
