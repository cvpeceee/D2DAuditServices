using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace D2DAudit.Domain;

public sealed class VendorProfile
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public DateOnly? ContractRenewalOn { get; set; }

    public string DataCategories { get; set; } = "";

    public bool Subprocessor { get; set; }

    public int ReviewCadenceMonths { get; set; } = 12;

    public DateOnly? LastReviewedOn { get; set; }

    public string ContactName { get; set; } = "";

    public string ContactEmail { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class VendorDiscoverySuggestion
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Source { get; set; } = "";

    public string Detail { get; set; } = "";

    public string SuggestedCriticality { get; set; } = "Medium";

    public string DataCategories { get; set; } = "";

    public string Status { get; set; } = "New";

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string DecidedBy { get; set; } = "";

    public Guid? VendorId { get; set; }
}

public sealed class VendorAssessment
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public string VendorName { get; set; } = "";

    public string TemplateCode { get; set; } = "";

    public string ContactName { get; set; } = "";

    public string ContactEmail { get; set; } = "";

    public string TokenHash { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public string Status { get; set; } = "Sent";

    public DateTimeOffset SentAt { get; set; }

    public string SentBy { get; set; } = "";

    public string DeliveryReceipt { get; set; } = "";

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string ReviewedBy { get; set; } = "";

    public string ReviewNote { get; set; } = "";

    public int? Score { get; set; }

    public string Rating { get; set; } = "";

    public List<VendorAssessmentAnswer> Answers { get; set; } = [];
}

public sealed class VendorAssessmentAnswer
{
    public Guid Id { get; set; }

    public Guid AssessmentId { get; set; }

    public VendorAssessment? Assessment { get; set; }

    public string QuestionCode { get; set; } = "";

    public string Answer { get; set; } = "";

    public string Comment { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class VendorReviewDocument
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public Guid? AssessmentId { get; set; }

    public string Kind { get; set; } = "Other";

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public string ObjectKey { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public string UploadedBy { get; set; } = "";

    public string ReportPeriod { get; set; } = "";

    public string Opinion { get; set; } = "";

    public string Exceptions { get; set; } = "";

    public string Cuecs { get; set; } = "";

    public string SummaryText { get; set; } = "";

    public string SummaryMode { get; set; } = "";

    public DateTimeOffset? SummarizedAt { get; set; }

    public string SignedOffBy { get; set; } = "";

    public DateTimeOffset? SignedOffAt { get; set; }

    public string SignOffNote { get; set; } = "";
}

public sealed record VendorInherentRisk(int Score, string Tier, int DefaultCadenceMonths);

public sealed record VendorReviewStatus(DateOnly NextReviewOn, string State, int DaysUntil);

public static class VendorRiskScoring
{
    public static readonly IReadOnlyDictionary<string, int> CategoryWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["CustomerData"] = 20,
        ["PersonalData"] = 20,
        ["Financial"] = 25,
        ["Health"] = 30,
        ["Credentials"] = 25,
        ["SourceCode"] = 15,
        ["EmployeeData"] = 10,
        ["None"] = 0,
    };

    public static int CriticalityWeight(VendorCriticality criticality) => criticality switch
    {
        VendorCriticality.Critical => 55,
        VendorCriticality.High => 40,
        VendorCriticality.Medium => 25,
        _ => 10,
    };

    public static VendorInherentRisk Inherent(VendorCriticality criticality, IEnumerable<string> dataCategories, bool subprocessor)
    {
        var data = dataCategories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(category => CategoryWeights.TryGetValue(category, out var weight) ? weight : 5);
        var score = Math.Clamp(CriticalityWeight(criticality) + Math.Min(data, 40) + (subprocessor ? 5 : 0), 0, 100);
        var tier = TierFor(score);
        var cadence = tier switch
        {
            "Tier1" => 6,
            "Tier2" => 12,
            "Tier3" => 12,
            _ => 24,
        };
        return new VendorInherentRisk(score, tier, cadence);
    }

    /// <summary>Guesses data categories from the free-text data access field when no profile exists yet.</summary>
    public static IReadOnlyList<string> InferCategories(string? dataAccess)
    {
        var text = (dataAccess ?? "").ToLowerInvariant();
        var found = new List<string>();
        void Add(string category, params string[] keywords)
        {
            if (keywords.Any(text.Contains))
            {
                found.Add(category);
            }
        }

        Add("CustomerData", "customer", "warehouse", "tenant data");
        Add("PersonalData", "personal", "pii", "contact", "email address");
        Add("Financial", "payment", "card", "billing", "financ", "invoice");
        Add("Credentials", "credential", "secret", "token", "key");
        Add("SourceCode", "source", "repositor", "code");
        Add("EmployeeData", "employee", "staff", "workforce");
        return found.Count == 0 ? ["None"] : found;
    }

    public static string TierFor(int score)
    {
        if (score >= 75)
        {
            return "Tier1";
        }

        if (score >= 50)
        {
            return "Tier2";
        }

        return score >= 25 ? "Tier3" : "Tier4";
    }

    public static VendorReviewStatus Review(DateOnly? lastReviewedOn, int cadenceMonths, DateOnly fallbackDueOn, DateOnly today)
    {
        var next = lastReviewedOn is DateOnly last ? last.AddMonths(Math.Clamp(cadenceMonths, 1, 60)) : fallbackDueOn;
        var days = next.DayNumber - today.DayNumber;
        return new VendorReviewStatus(next, Window(days, 30, "Overdue", "DueSoon", "Current"), days);
    }

    public static string RenewalState(DateOnly? renewalOn, DateOnly today)
    {
        if (renewalOn is not DateOnly renewal)
        {
            return "None";
        }

        return Window(renewal.DayNumber - today.DayNumber, 60, "Lapsed", "Upcoming", "Current");
    }

    private static string Window(int days, int soon, string past, string near, string later)
    {
        if (days < 0)
        {
            return past;
        }

        return days <= soon ? near : later;
    }
}

public sealed record QuestionnaireQuestion(string Code, string Domain, string Text, int Weight, bool Critical);

public sealed record QuestionnaireTemplate(string Code, string Name, string Description, IReadOnlyList<QuestionnaireQuestion> Questions);

public sealed record QuestionnaireScore(int Percent, string Rating, int Answered, int Total, IReadOnlyList<string> Flagged);

public static class VendorQuestionnaireLibrary
{
    public static readonly IReadOnlyList<string> Answers = ["Yes", "Partial", "No", "NotApplicable"];

    public static readonly QuestionnaireTemplate Core = new(
        "core-security",
        "Core vendor security review",
        "A short security review covering governance, access, data protection, operations, and resilience.",
        [
            new("GOV-1", "Governance", "Do you keep a written information security program that leadership approves at least once a year?", 3, false),
            new("GOV-2", "Governance", "Is a named person accountable for security across your company?", 2, false),
            new("GOV-3", "Governance", "Do you hold a current independent security attestation, such as a SOC 2 Type II report or ISO 27001 certificate?", 4, true),
            new("HR-1", "People", "Do staff with access to customer data pass a background check where local law allows?", 2, false),
            new("HR-2", "People", "Do all staff complete security awareness training when they join and every year after?", 2, false),
            new("AC-1", "Access", "Is multi-factor sign-in required for every account that can reach customer data or production systems?", 4, true),
            new("AC-2", "Access", "Do you review who has access to production systems at least every quarter?", 3, false),
            new("AC-3", "Access", "Is access removed within one business day when someone leaves?", 3, true),
            new("DP-1", "Data", "Is customer data encrypted while stored, using industry standard algorithms?", 4, true),
            new("DP-2", "Data", "Is customer data encrypted in transit with TLS 1.2 or newer?", 4, true),
            new("DP-3", "Data", "Can you delete or return customer data when the contract ends, and confirm it in writing?", 3, false),
            new("DP-4", "Data", "Do you keep a current list of subprocessors and tell customers before adding one?", 2, false),
            new("OP-1", "Operations", "Do you scan for vulnerabilities and fix critical findings within a defined window?", 3, false),
            new("OP-2", "Operations", "Has an independent firm tested your product for security weaknesses in the last twelve months?", 3, false),
            new("OP-3", "Operations", "Are code changes reviewed by someone other than the author before release?", 2, false),
            new("OP-4", "Operations", "Do you keep security logs for at least one year and watch them for alerts?", 2, false),
            new("IR-1", "Incidents", "Do you have a written incident response plan that you test at least once a year?", 3, false),
            new("IR-2", "Incidents", "Will you notify customers of a breach affecting their data within 72 hours of confirming it?", 4, true),
            new("BC-1", "Resilience", "Do you back up customer data and test restoring it at least once a year?", 3, false),
            new("BC-2", "Resilience", "Do you have a business continuity plan with recovery targets for the service you provide us?", 2, false),
        ]);

    public static readonly QuestionnaireTemplate Privacy = new(
        "privacy-addendum",
        "Privacy addendum",
        "Follow-up questions for vendors that process personal data.",
        [
            new("PR-1", "Privacy", "Do you process personal data only on documented customer instructions?", 4, true),
            new("PR-2", "Privacy", "Do you sign a data processing agreement that covers international transfers?", 3, false),
            new("PR-3", "Privacy", "Can you help us respond to data subject requests within 30 days?", 3, false),
            new("PR-4", "Privacy", "Do you keep a record of processing activities for the service you provide us?", 2, false),
            new("PR-5", "Privacy", "Is personal data minimized and kept only as long as the contract requires?", 2, false),
        ]);

    public static readonly IReadOnlyList<QuestionnaireTemplate> Templates = [Core, Privacy];

    public static QuestionnaireTemplate? Find(string? code) =>
        Templates.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool ValidAnswer(string? answer) => Answers.Contains(answer ?? "", StringComparer.Ordinal);

    public static QuestionnaireScore Score(QuestionnaireTemplate template, IReadOnlyDictionary<string, string> answers)
    {
        double earned = 0;
        double possible = 0;
        var answered = 0;
        var flagged = new List<string>();
        var criticalMiss = false;
        foreach (var question in template.Questions)
        {
            if (!answers.TryGetValue(question.Code, out var answer) || !ValidAnswer(answer))
            {
                possible += question.Weight;
                continue;
            }

            answered++;
            if (answer == "NotApplicable")
            {
                continue;
            }

            possible += question.Weight;
            var credit = answer switch
            {
                "Yes" => 1.0,
                "Partial" => 0.5,
                _ => 0.0,
            };
            earned += question.Weight * credit;
            if (credit < 1.0)
            {
                flagged.Add(question.Code);
                if (question.Critical && credit == 0.0)
                {
                    criticalMiss = true;
                }
            }
        }

        var percent = possible <= 0 ? 100 : (int)Math.Round(earned * 100 / possible, MidpointRounding.AwayFromZero);
        return new QuestionnaireScore(percent, Rate(percent, criticalMiss), answered, template.Questions.Count, flagged);
    }

    public static string Rate(int percent, bool criticalMiss)
    {
        if (criticalMiss || percent < 65)
        {
            return "High";
        }

        return percent >= 85 ? "Low" : "Moderate";
    }
}

public enum PortalTokenState
{
    Valid,
    Invalid,
    Expired,
}

public static class VendorPortalTokens
{
    public static (string Token, string Hash) Create(Guid tenantId)
    {
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return ($"{tenantId:N}.{secret}", Hash(secret));
    }

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool TryParse(string? token, out Guid tenantId, out string secret)
    {
        tenantId = Guid.Empty;
        secret = "";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return false;
        }

        var dot = token.IndexOf('.');
        if (dot != 32 || !Guid.TryParseExact(token[..dot], "N", out tenantId))
        {
            return false;
        }

        secret = token[(dot + 1)..];
        return secret.Length >= 32 && secret.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    }

    public static PortalTokenState Verify(string storedHash, string secret, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var expected = Encoding.ASCII.GetBytes(storedHash ?? "");
        var actual = Encoding.ASCII.GetBytes(Hash(secret ?? ""));
        var match = expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        if (!match)
        {
            return PortalTokenState.Invalid;
        }

        return now >= expiresAt ? PortalTokenState.Expired : PortalTokenState.Valid;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record Soc2Summary(string ReportPeriod, string Opinion, IReadOnlyList<string> Exceptions, IReadOnlyList<string> Cuecs, string ReportType);

public static partial class Soc2Summarizer
{
    public const string NotFound = "Not stated in the document";

    public static Soc2Summary Summarize(string? text)
    {
        var body = Normalize(text);
        var lower = body.ToLowerInvariant();
        var lines = body.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var exceptions = lines
            .Where(line => line.Contains("exception", StringComparison.OrdinalIgnoreCase)
                && !NoExceptionPattern().IsMatch(line))
            .Select(Clip)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        return new Soc2Summary(Period(body), Opinion(lower), exceptions, Cuecs(lines), ReportType(lower));
    }

    private static string Period(string body)
    {
        var range = PeriodPattern().Match(body);
        if (range.Success)
        {
            return $"{range.Groups["from"].Value.Trim()} to {range.Groups["to"].Value.Trim()}";
        }

        var asOf = AsOfPattern().Match(body);
        return asOf.Success ? $"As of {asOf.Groups["on"].Value.Trim()}" : NotFound;
    }

    private static string Opinion(string lower)
    {
        if (lower.Contains("adverse opinion"))
        {
            return "Adverse";
        }

        if (lower.Contains("disclaimer of opinion") || lower.Contains("do not express an opinion"))
        {
            return "Disclaimer";
        }

        if ((lower.Contains("qualified opinion") && !lower.Contains("unqualified opinion"))
            || (lower.Contains("except for") && lower.Contains("opinion")))
        {
            return "Qualified";
        }

        if (lower.Contains("unqualified") || lower.Contains("unmodified") || lower.Contains("present fairly") || lower.Contains("presents fairly"))
        {
            return "Unqualified";
        }

        return NotFound;
    }

    private static string ReportType(string lower)
    {
        if (lower.Contains("type ii") || lower.Contains("type 2"))
        {
            return "SOC 2 Type II";
        }

        if (lower.Contains("type i") || lower.Contains("type 1"))
        {
            return "SOC 2 Type I";
        }

        return lower.Contains("soc 2") ? "SOC 2" : "Unknown";
    }

    private static string[] Cuecs(string[] lines)
    {
        var cuecs = new List<string>();
        var inCuecs = false;
        foreach (var line in lines)
        {
            if (line.Contains("complementary user entity control", StringComparison.OrdinalIgnoreCase)
                || line.Contains("CUEC", StringComparison.Ordinal))
            {
                inCuecs = true;
                continue;
            }

            if (!inCuecs)
            {
                continue;
            }

            if (BulletPattern().IsMatch(line))
            {
                cuecs.Add(Clip(BulletPattern().Replace(line, "")));
                if (cuecs.Count >= 15)
                {
                    break;
                }

                continue;
            }

            if (cuecs.Count > 0 && line.EndsWith(':'))
            {
                break;
            }
        }

        return cuecs.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string ExtractText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        var raw = Encoding.UTF8.GetString(bytes);
        if (!raw.StartsWith("%PDF", StringComparison.Ordinal))
        {
            return raw;
        }

        var builder = new StringBuilder();
        foreach (Match match in PdfTextPattern().Matches(raw))
        {
            builder.Append(match.Groups["t"].Value.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\"));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string Normalize(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Clip(string value) => value.Length > 300 ? value[..300] : value;

    [GeneratedRegex(@"(?:for\s+the\s+period|period\s+of|period)\s+(?:from\s+)?(?<from>[A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})\s+(?:to|through|and\s+ending|-)\s+(?<to>[A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase, 500)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"as\s+of\s+(?<on>[A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase, 500)]
    private static partial Regex AsOfPattern();

    [GeneratedRegex(@"\bno\s+(?:\w+\s+){0,2}exceptions?\b", RegexOptions.IgnoreCase, 500)]
    private static partial Regex NoExceptionPattern();

    [GeneratedRegex(@"^(?:[-*\u2022]|\d+[.)])\s+", RegexOptions.None, 500)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\((?<t>(?:[^()\\]|\\.){2,400})\)\s*Tj", RegexOptions.None, 1000)]
    private static partial Regex PdfTextPattern();
}
