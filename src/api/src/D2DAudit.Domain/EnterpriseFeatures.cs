using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace D2DAudit.Domain;

public sealed class ApiKeyRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Prefix { get; set; } = "";

    public string KeyHash { get; set; } = "";

    public string Scopes { get; set; } = "read:controls,read:evidence";

    public string CreatedByEmail { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class WebhookSubscription
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public string Events { get; set; } = "test.failed,exposure.critical";

    public string SigningSecretHash { get; set; } = "";

    public string SigningSecretEncrypted { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record TrustReceiptFramework(string Key, string Name, int Passing, int Total, int Percent);

public sealed record TrustReceiptDocument(
    string Slug,
    string TenantName,
    string Region,
    int RetentionYears,
    DateTimeOffset IssuedAtUtc,
    IReadOnlyList<TrustReceiptFramework> Frameworks,
    int ChainEntryCount,
    long LatestChainSequence,
    string ChainHeadHash,
    string ChainStatus,
    string CanonicalPayload,
    string ReceiptSha256);

public sealed record SimulatorRequirementInput(string Code, string Title, string Summary, bool Mapped, bool Passing);

public sealed record SimulatorGapClause(string Code, string Title, string Summary, string SuggestedControlCode);

public sealed record FrameworkSimulationResult(
    string FrameworkKey,
    string FrameworkName,
    int TotalRequirements,
    int CoveredRequirements,
    int PassingRequirements,
    int OverlapPercent,
    int ReadinessPercent,
    IReadOnlyList<SimulatorGapClause> Gaps);

public sealed record CustomRequirementRow(string Code, string Title, string Summary);

public sealed record AssistantFact(string Kind, string Code, string Title, string Detail, string Href);

public sealed record AssistantCitation(string Kind, string Code, string Title, string Href);

public sealed record AssistantReply(
    string Answer,
    string Mode,
    IReadOnlyList<AssistantCitation> Citations,
    IReadOnlyList<string> SuggestedRoutes);

public static class TrustReceiptBuilder
{
    public static TrustReceiptDocument Build(
        string slug,
        string tenantName,
        string region,
        int retentionYears,
        DateTimeOffset issuedAtUtc,
        IReadOnlyList<TrustReceiptFramework> frameworks,
        int chainEntryCount,
        long latestSequence,
        string chainHeadHash,
        bool chainVerified = true)
    {
        var ordered = frameworks.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var status = "Broken";
        if (chainEntryCount <= 0 || string.IsNullOrWhiteSpace(chainHeadHash))
        {
            status = "Empty";
        }
        else if (chainVerified)
        {
            status = "Verified";
        }
        var fwSummary = string.Join(';', ordered.Select(item =>
            $"{CleanField(item.Key)}:{item.Passing.ToString(CultureInfo.InvariantCulture)}/{item.Total.ToString(CultureInfo.InvariantCulture)}"));
        var canonical = string.Join(
            '|',
            "d2d-trust-receipt-v1",
            CleanField(slug),
            CleanField(tenantName),
            CleanField(region),
            retentionYears.ToString(CultureInfo.InvariantCulture),
            issuedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            fwSummary,
            chainEntryCount.ToString(CultureInfo.InvariantCulture),
            latestSequence.ToString(CultureInfo.InvariantCulture),
            CleanField(chainHeadHash ?? ""),
            status);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new TrustReceiptDocument(
            slug,
            tenantName,
            region,
            retentionYears,
            issuedAtUtc,
            ordered,
            chainEntryCount,
            latestSequence,
            chainHeadHash ?? "",
            status,
            canonical,
            digest);
    }

    private static string CleanField(string value) =>
        (value ?? "").Replace("|", "/", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    public static bool Verify(string canonicalPayload, string receiptSha256)
    {
        if (string.IsNullOrWhiteSpace(canonicalPayload) || string.IsNullOrWhiteSpace(receiptSha256))
        {
            return false;
        }

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
        var actual = receiptSha256.Trim().ToLowerInvariant();
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
    }
}

public static class FrameworkSimulator
{
    public static FrameworkSimulationResult Analyze(
        string frameworkKey,
        string frameworkName,
        IReadOnlyList<SimulatorRequirementInput> leaves)
    {
        var total = leaves.Count;
        var covered = leaves.Count(item => item.Mapped);
        var passing = leaves.Count(item => item.Passing);
        var overlapPercent = total == 0 ? 0 : (int)Math.Round(covered * 100.0 / total, MidpointRounding.AwayFromZero);
        var readinessPercent = total == 0 ? 0 : (int)Math.Round(passing * 100.0 / total, MidpointRounding.AwayFromZero);
        var prefix = BuildPrefix(frameworkKey);
        var gaps = leaves
            .Where(item => !item.Mapped)
            .Select((item, index) => new SimulatorGapClause(
                item.Code,
                item.Title,
                item.Summary,
                $"{prefix}-{(index + 1).ToString("D2", CultureInfo.InvariantCulture)}"))
            .ToArray();
        return new FrameworkSimulationResult(
            frameworkKey,
            frameworkName,
            total,
            covered,
            passing,
            overlapPercent,
            readinessPercent,
            gaps);
    }

    private static string BuildPrefix(string key)
    {
        var letters = new string(key.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (letters.Length == 0)
        {
            return "GAP";
        }

        return letters.Length <= 4 ? letters : letters[..4];
    }
}

public static partial class CustomFrameworkCsv
{
    public static IReadOnlyList<CustomRequirementRow> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var start = lines[0].StartsWith("code,", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var rows = new List<CustomRequirementRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = start; i < lines.Length; i++)
        {
            var parts = SplitLine(lines[i]);
            if (parts.Count < 2)
            {
                continue;
            }

            var code = parts[0].Trim();
            var title = parts[1].Trim();
            var summary = parts.Count > 2 && !string.IsNullOrWhiteSpace(parts[2])
                ? parts[2].Trim()
                : title;
            if (code.Length is 0 or > 64 || title.Length is 0 or > 300 || summary.Length > 2000)
            {
                continue;
            }

            summary = ShallWord().Replace(summary, "must");

            if (seen.Add(code))
            {
                rows.Add(new CustomRequirementRow(code, title, summary));
            }
        }

        return rows;
    }

    [GeneratedRegex("\\bshall\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShallWord();

    private static List<string> SplitLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < line.Length)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }

            i++;
        }

        cells.Add(current.ToString());
        return cells;
    }
}

public static class GroundedAssistant
{
    public static AssistantReply Answer(string question, IReadOnlyList<AssistantFact> facts, string scopeLabel)
    {
        var clean = (question ?? "").Trim();
        if (clean.Length == 0)
        {
            return new AssistantReply(
                "Ask a question about failing controls, frameworks, public exposures, attack paths, DSPM stores, FinOps savings, policies, or evidence.",
                "grounded-retrieval",
                [],
                ["/controls", "/frameworks", "/exposure"]);
        }

        var tokens = Tokenize(clean);
        var scored = facts
            .Select(fact => (Fact: fact, Score: ScoreFact(fact, tokens, clean)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Fact.Code, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        if (scored.Length == 0)
        {
            var fallback = facts.Take(3).ToArray();
            var fallbackCitations = fallback.Select(item => new AssistantCitation(item.Kind, item.Code, item.Title, item.Href)).ToArray();
            return new AssistantReply(
                $"No direct match was found in {scopeLabel} for \"{clean}\". Here are the top active items currently visible in your scope: "
                + string.Join(" | ", fallback.Select(item => $"[{item.Code}] {item.Title}: {item.Detail}")),
                "grounded-retrieval",
                fallbackCitations,
                fallback.Select(item => item.Href).Distinct(StringComparer.Ordinal).Take(4).ToArray());
        }

        var citations = scored
            .Select(item => new AssistantCitation(item.Fact.Kind, item.Fact.Code, item.Fact.Title, item.Fact.Href))
            .ToArray();
        var summaryLines = scored.Select(item => $"• [{item.Fact.Code}] {item.Fact.Title} — {item.Fact.Detail}");
        var answer = $"Grounded in {scopeLabel} ({scored.Length.ToString(CultureInfo.InvariantCulture)} matched records):\n" + string.Join('\n', summaryLines);
        var routes = scored.Select(item => item.Fact.Href).Distinct(StringComparer.Ordinal).Take(4).ToArray();
        return new AssistantReply(answer, "grounded-retrieval", citations, routes);
    }

    public static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static int ScoreFact(AssistantFact fact, HashSet<string> tokens, string rawQuestion)
    {
        var hay = (fact.Kind + " " + fact.Code + " " + fact.Title + " " + fact.Detail).ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (fact.Code.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }
            else if (hay.Contains(token, StringComparison.Ordinal))
            {
                score += 3;
            }
        }

        var q = rawQuestion.ToLowerInvariant();
        if ((q.Contains("fail", StringComparison.Ordinal) || q.Contains("broken", StringComparison.Ordinal) || q.Contains("gap", StringComparison.Ordinal))
            && (hay.Contains("failing", StringComparison.Ordinal) || hay.Contains("open", StringComparison.Ordinal) || hay.Contains("critical", StringComparison.Ordinal)))
        {
            score += 4;
        }

        if ((q.Contains("cost", StringComparison.Ordinal) || q.Contains("finops", StringComparison.Ordinal) || q.Contains("spend", StringComparison.Ordinal) || q.Contains("save", StringComparison.Ordinal))
            && fact.Kind == "FinOps")
        {
            score += 6;
        }

        if ((q.Contains("attack", StringComparison.Ordinal) || q.Contains("blast", StringComparison.Ordinal) || q.Contains("path", StringComparison.Ordinal))
            && fact.Kind == "AttackPath")
        {
            score += 6;
        }

        if ((q.Contains("pii", StringComparison.Ordinal) || q.Contains("data", StringComparison.Ordinal) || q.Contains("dspm", StringComparison.Ordinal) || q.Contains("encrypt", StringComparison.Ordinal))
            && fact.Kind == "DSPM")
        {
            score += 5;
        }

        return score;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_')
            {
                current.Append(ch);
            }
            else if (current.Length >= 3)
            {
                words.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Clear();
            }
        }

        if (current.Length >= 3)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}

public static class ApiKeyTokens
{
    public const string Prefix = "d2d_api_";

    public static string Create(Guid tenantId)
    {
        var secret = RandomNumberGenerator.GetHexString(32, lowercase: true);
        return $"{Prefix}{tenantId:N}_{secret}";
    }

    public static bool IsApiKey(string? token) =>
        !string.IsNullOrWhiteSpace(token) && token.Trim().StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryParseTenantId(string? token, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[Prefix.Length..];
        var sep = body.IndexOf('_', StringComparison.Ordinal);
        if (sep != 32 || body.Length < 48)
        {
            return false;
        }

        return Guid.TryParseExact(body[..32], "N", out tenantId);
    }

    public static bool CanWrite(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes))
        {
            return false;
        }

        return scopes.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(scope => scope.StartsWith("write:", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "*", StringComparison.Ordinal));
    }
}

public static class WebhookSigning
{
    public static string Sign(string secret, long unixTimestamp, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var message = Encoding.UTF8.GetBytes($"{unixTimestamp.ToString(CultureInfo.InvariantCulture)}.{payload}");
        var mac = HMACSHA256.HashData(key, message);
        var hex = Convert.ToHexString(mac).ToLowerInvariant();
        return $"t={unixTimestamp.ToString(CultureInfo.InvariantCulture)},v1={hex}";
    }

    public static bool Verify(string secret, long unixTimestamp, string payload, string headerValue)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var expected = Sign(secret, unixTimestamp, payload);
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(headerValue.Trim());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string EventNameForTrigger(string trigger) => trigger switch
    {
        "TestFailed" => "test.failed",
        "ControlNotReady" => "control.not_ready",
        "RiskCreated" => "risk.created",
        "ReviewDue" => "review.due",
        "ExposureCritical" => "exposure.critical",
        _ => trigger.Trim().ToLowerInvariant(),
    };

    public static bool MatchesEvent(string? subscribedEvents, string eventName)
    {
        if (string.IsNullOrWhiteSpace(subscribedEvents))
        {
            return false;
        }

        var tokens = subscribedEvents.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token == "*"
                || string.Equals(token, eventName, StringComparison.OrdinalIgnoreCase)
                || (token.EndsWith(".*", StringComparison.Ordinal)
                    && eventName.StartsWith(token[..^1], StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
