using System.Globalization;
using System.Text;

namespace D2DAudit.Domain;

public sealed class LegalEntityRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Jurisdiction { get; set; } = "";

    public string Region { get; set; } = "us-east-1";

    public int Headcount { get; set; }

    public int ControlsPassing { get; set; }

    public int ControlsTotal { get; set; }

    public string PrimaryFramework { get; set; } = "SOC 2 Type II";

    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PrivacyAssessment
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Kind { get; set; } = "RoPA";

    public string ActivityName { get; set; } = "";

    public string DataSubjects { get; set; } = "";

    public string LawfulBasis { get; set; } = "";

    public string TransferMechanism { get; set; } = "";

    public string RiskLevel { get; set; } = "Medium";

    public string Status { get; set; } = "InReview";

    public string OwnerEmail { get; set; } = "";

    public DateOnly ReviewDueOn { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiModelRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string ModelName { get; set; } = "";

    public string Provider { get; set; } = "";

    public string EuAiActRiskTier { get; set; } = "Limited";

    public string TrainingDataProvenance { get; set; } = "";

    public string Guardrails { get; set; } = "";

    public string RedTeamStatus { get; set; } = "Passed";

    public string LinkedControlCode { get; set; } = "AI-01";

    public string OwnerEmail { get; set; } = "";

    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class PhishingCampaign
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Source { get; set; } = "KnowBe4";

    public int SentCount { get; set; }

    public int ClickedCount { get; set; }

    public int ReportedCount { get; set; }

    public string RemedialTrainingStatus { get; set; } = "Auto-assigned";

    public DateTimeOffset RanAt { get; set; }
}

public sealed class AttackSurfaceAsset
{
    public Guid Id { get; set; }

    public string Hostname { get; set; } = "";

    public string IpAddress { get; set; } = "";

    public string Service { get; set; } = "";

    public int Port { get; set; }

    public int TlsDaysRemaining { get; set; }

    public bool ShadowIt { get; set; }

    public string Severity { get; set; } = "Medium";

    public string Status { get; set; } = "Open";

    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class AuditorFirmRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string FirmName { get; set; } = "";

    public string Accreditation { get; set; } = "";

    public string Specialties { get; set; } = "";

    public string Regions { get; set; } = "";

    public string LeadAuditorEmail { get; set; } = "";

    public bool Invited { get; set; }

    public DateTimeOffset? InvitedAt { get; set; }
}

public sealed class RemediationPullRequest
{
    public Guid Id { get; set; }

    public string ControlCode { get; set; } = "";

    public string TargetResource { get; set; } = "";

    public string Repository { get; set; } = "";

    public string BranchName { get; set; } = "";

    public string Title { get; set; } = "";

    public string ArtifactType { get; set; } = "Terraform";

    public string UnifiedDiff { get; set; } = "";

    public int PrNumber { get; set; }

    public string PrUrl { get; set; } = "";

    public string Mode { get; set; } = "signed-receipt";

    public string Status { get; set; } = "Open";

    public Guid? EvidenceId { get; set; }

    public string EvidenceSha256 { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record EntityRollupSummary(
    int EntityCount,
    int TotalHeadcount,
    int ControlsPassing,
    int ControlsTotal,
    int ConsolidatedReadinessPercent);

public sealed record GeneratedRemediationPatch(
    string BranchName,
    string Title,
    string ArtifactType,
    string UnifiedDiff);

public sealed record TimeMachineSnapshot(
    DateOnly Date,
    int PassingTests,
    int FailingTests,
    int TestPassPercent,
    int EstimatedReadinessPercent,
    int ChainEntryCount,
    long LatestChainSequence,
    string LatestChainHead);

public sealed record TimeMachineComparison(
    TimeMachineSnapshot AsOf,
    TimeMachineSnapshot CompareTo,
    int PassPercentDelta,
    int ReadinessDelta,
    int ChainEntriesDelta,
    string Summary);

public static class EntityRollupCalculator
{
    public static EntityRollupSummary Summarize(IReadOnlyList<LegalEntityRecord> entities)
    {
        if (entities.Count == 0)
        {
            return new EntityRollupSummary(0, 0, 0, 0, 0);
        }

        var headcount = entities.Sum(item => Math.Max(0, item.Headcount));
        var passing = entities.Sum(item => Math.Clamp(item.ControlsPassing, 0, Math.Max(0, item.ControlsTotal)));
        var total = entities.Sum(item => Math.Max(0, item.ControlsTotal));
        var readiness = total == 0 ? 0 : (int)Math.Round(passing * 100.0 / total, MidpointRounding.AwayFromZero);
        return new EntityRollupSummary(entities.Count, headcount, passing, total, readiness);
    }

    public static int ReadinessPercent(int passing, int total) =>
        total <= 0 ? 0 : (int)Math.Round(Math.Clamp(passing, 0, total) * 100.0 / total, MidpointRounding.AwayFromZero);
}

public static class PhishingMetrics
{
    public static double RatePercent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0.0;
        }

        var bounded = Math.Clamp(numerator, 0, denominator);
        return Math.Round(bounded * 100.0 / denominator, 1, MidpointRounding.AwayFromZero);
    }

    public static string RiskBand(double clickRatePercent) => clickRatePercent switch
    {
        >= 10.0 => "High",
        >= 4.0 => "Medium",
        _ => "Low",
    };
}

public static class RemediationPrBuilder
{
    public static GeneratedRemediationPatch Generate(
        string controlCode,
        string targetResource,
        string repository,
        string artifactType,
        string? customFix = null)
    {
        var code = string.IsNullOrWhiteSpace(controlCode) ? "CFG-01" : controlCode.Trim().ToUpperInvariant();
        var resource = string.IsNullOrWhiteSpace(targetResource) ? "cloud-resource" : targetResource.Trim();
        var normalizedType = NormalizeType(artifactType);
        var slug = Slugify(resource);
        var branch = $"d2d/fix-{Slugify(code)}-{slug}";
        if (branch.Length > 110)
        {
            branch = branch[..110];
        }

        var title = $"fix({code}): remediate {resource} via {normalizedType}";
        var diff = BuildDiff(code, resource, normalizedType, customFix);
        return new GeneratedRemediationPatch(branch, title, normalizedType, diff);
    }

    public static string NormalizeType(string? artifactType) => (artifactType ?? "").Trim().ToLowerInvariant() switch
    {
        "dockerfile" => "Dockerfile",
        "githubworkflow" or "workflow" or "github-workflow" => "GitHubWorkflow",
        "iampolicy" or "iam" or "iam-policy" => "IamPolicy",
        _ => "Terraform",
    };

    private static string BuildDiff(string controlCode, string resource, string artifactType, string? customFix)
    {
        var safeName = EscapeHcl(resource);
        var tfId = Slugify(resource).Replace('-', '_');
        if (!string.IsNullOrWhiteSpace(customFix))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- a/infra/{tfId}.tf");
            sb.AppendLine($"+++ b/infra/{tfId}.tf");
            sb.AppendLine($"@@ -1,4 +1,8 @@ # Control {controlCode}");
            foreach (var line in customFix.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Take(24))
            {
                sb.Append('+').AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        return artifactType switch
        {
            "Dockerfile" => string.Join('\n',
                "--- a/Dockerfile",
                "+++ b/Dockerfile",
                $"@@ -1,5 +1,7 @@ # Control {controlCode} ({safeName})",
                "-FROM debian:12.4-slim",
                "+FROM debian:12.8-slim",
                " WORKDIR /app",
                "-COPY . .",
                "+COPY --chown=nonroot:nonroot dist/ ./dist/",
                "+USER nonroot:nonroot"),
            "GitHubWorkflow" => string.Join('\n',
                "--- a/.github/workflows/deploy.yml",
                "+++ b/.github/workflows/deploy.yml",
                $"@@ -4,6 +4,11 @@ # Control {controlCode} ({safeName})",
                " permissions:",
                "-  contents: write",
                "+  contents: read",
                "+  id-token: write",
                " jobs:",
                "   release:",
                "+    environment: production-approved",
                "     steps:",
                "+      - name: Require peer review & signed provenance",
                "+        run: echo \"Verified review gate for " + safeName + "\""),
            "IamPolicy" => string.Join('\n',
                $"--- a/iam/{tfId}-policy.json",
                $"+++ b/iam/{tfId}-policy.json",
                $"@@ -3,7 +3,10 @@ # Control {controlCode} ({safeName})",
                "     \"Effect\": \"Allow\",",
                "-    \"Action\": \"*\",",
                "-    \"Resource\": \"*\"",
                "+    \"Action\": [\"s3:GetObject\", \"kms:Decrypt\"],",
                "+    \"Resource\": \"arn:aws:s3:::" + safeName + "/*\",",
                "+    \"Condition\": { \"Bool\": { \"aws:MultiFactorAuthPresent\": \"true\" } }"),
            _ => string.Join('\n',
                $"--- a/infra/terraform/{tfId}.tf",
                $"+++ b/infra/terraform/{tfId}.tf",
                $"@@ -1,6 +1,11 @@ # Control {controlCode} ({safeName})",
                $" resource \"aws_vpc_security_group_ingress_rule\" \"{tfId}_restricted\" {{",
                $"   security_group_id = \"{safeName}\"",
                "-  cidr_ipv4         = \"0.0.0.0/0\"",
                "+  cidr_ipv4         = \"10.20.0.0/16\"",
                "   ip_protocol       = \"tcp\"",
                "+  description       = \"Restricted by D2D remediation PR for " + controlCode + "\"",
                " }"),
        };
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder();
        var prevDash = false;
        foreach (var ch in (value ?? "").ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevDash = false;
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? "resource" : result;
    }

    private static string EscapeHcl(string value) =>
        (value ?? "").Replace("\\", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}

public static class TimeMachineCalculator
{
    public static TimeMachineComparison Compare(
        DateOnly asOf,
        DateOnly compareTo,
        IReadOnlyList<CheckWeek> checkWeeks,
        IReadOnlyList<ChainEntry> chainEntries,
        int currentPassingControls,
        int totalControls)
    {
        var orderedWeeks = checkWeeks.OrderBy(item => item.WeekOf).ToArray();
        var orderedChain = chainEntries.OrderBy(item => item.Sequence).ToArray();
        var currentReadiness = totalControls <= 0
            ? 0
            : (int)Math.Round(Math.Clamp(currentPassingControls, 0, totalControls) * 100.0 / totalControls, MidpointRounding.AwayFromZero);

        var latestWeek = orderedWeeks.LastOrDefault();
        var latestPassPct = latestWeek is null ? currentReadiness : PassPercent(latestWeek.Passing, latestWeek.Failing);

        var first = BuildSnapshot(asOf, orderedWeeks, orderedChain, currentReadiness, latestPassPct);
        var second = BuildSnapshot(compareTo, orderedWeeks, orderedChain, currentReadiness, latestPassPct);

        var passDelta = second.TestPassPercent - first.TestPassPercent;
        var readinessDelta = second.EstimatedReadinessPercent - first.EstimatedReadinessPercent;
        var chainDelta = second.ChainEntryCount - first.ChainEntryCount;

        var direction = readinessDelta >= 0 ? "improved" : "regressed";
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Posture {direction} by {Math.Abs(readinessDelta)}% ({first.EstimatedReadinessPercent}% on {first.Date:yyyy-MM-dd} -> {second.EstimatedReadinessPercent}% on {second.Date:yyyy-MM-dd}); EvidenceChain grew by {(chainDelta >= 0 ? "+" : "")}{chainDelta} verified entries.");

        return new TimeMachineComparison(first, second, passDelta, readinessDelta, chainDelta, summary);
    }

    private static TimeMachineSnapshot BuildSnapshot(
        DateOnly targetDate,
        CheckWeek[] orderedWeeks,
        ChainEntry[] orderedChain,
        int currentReadiness,
        int latestPassPct)
    {
        var week = orderedWeeks.LastOrDefault(item => item.WeekOf <= targetDate) ?? orderedWeeks.FirstOrDefault();
        var passing = week?.Passing ?? 0;
        var failing = week?.Failing ?? 0;
        var passPct = PassPercent(passing, failing);
        var estimatedReadiness = week is null
            ? currentReadiness
            : Math.Clamp(currentReadiness - (latestPassPct - passPct), 0, 100);

        var cutoff = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var upToDate = orderedChain.Where(item => item.CreatedAt <= cutoff).ToArray();
        var latestChain = upToDate.LastOrDefault();

        return new TimeMachineSnapshot(
            targetDate,
            passing,
            failing,
            passPct,
            estimatedReadiness,
            upToDate.Length,
            latestChain?.Sequence ?? 0,
            latestChain?.Head ?? "");
    }

    private static int PassPercent(int passing, int failing)
    {
        var total = Math.Max(0, passing) + Math.Max(0, failing);
        return total == 0 ? 0 : (int)Math.Round(Math.Max(0, passing) * 100.0 / total, MidpointRounding.AwayFromZero);
    }
}
