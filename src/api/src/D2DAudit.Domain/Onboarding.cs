using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace D2DAudit.Domain;

public sealed class SignupRequestRecord
{
    public Guid Id { get; set; }

    public string CompanyName { get; set; } = "";

    public string Email { get; set; } = "";

    public string EmailDomain { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Region { get; set; } = "US";

    public string? PasswordHash { get; set; }

    public string TokenHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string Status { get; set; } = SignupStatus.PendingVerification;

    public string? Error { get; set; }
}

public static class SignupStatus
{
    public const string PendingVerification = "PendingVerification";

    public const string Provisioning = "Provisioning";

    public const string Ready = "Ready";

    public const string Failed = "Failed";
}

public sealed class MemberInvite
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Email { get; set; } = "";

    public TenantRole Role { get; set; }

    public string TokenHash { get; set; } = "";

    public Guid InvitedByUserId { get; set; }

    public string InvitedByName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string State(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
        {
            return "Accepted";
        }

        if (RevokedAt is not null)
        {
            return "Revoked";
        }

        return OneTimeTokens.IsExpired(ExpiresAt, now) ? "Expired" : "Pending";
    }
}

public sealed class TenantOnboarding
{
    public Guid TenantId { get; set; }

    public string CompanySize { get; set; } = "";

    public string Industry { get; set; } = "";

    public string CloudProviders { get; set; } = "";

    public string DataTypes { get; set; } = "";

    public string Frameworks { get; set; } = "";

    public string ScopedControls { get; set; } = "";

    public string CompletedSteps { get; set; } = "";

    public DateOnly? AuditTargetDate { get; set; }

    public bool Dismissed { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TenantSubscription
{
    public Guid TenantId { get; set; }

    public string Plan { get; set; } = PlanCatalog.Starter;

    public string Status { get; set; } = "Active";

    public string Provider { get; set; } = "mock";

    public string? CustomerId { get; set; }

    public string? SubscriptionId { get; set; }

    public DateOnly? CurrentPeriodEnd { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TenantSecuritySettings
{
    public Guid TenantId { get; set; }

    public bool SsoEnforced { get; set; }

    public string? ScimTokenHash { get; set; }

    public string? ScimTokenPrefix { get; set; }

    public DateTimeOffset? ScimTokenCreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BillingEventRecord
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public Guid? TenantId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed record SignupInput(string CompanyName, string Email, string? Password, string Region, bool PasswordRequired);

public static partial class SignupRules
{
    public const int MinimumPasswordLength = 12;

    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    private static readonly HashSet<string> Disposable = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "yopmail.com", "guerrillamail.com", "guerrillamail.net", "sharklasers.com", "10minutemail.com",
        "tempmail.com", "temp-mail.org", "throwawaymail.com", "trashmail.com", "getnada.com", "dispostable.com",
        "maildrop.cc", "mintemail.com", "fakeinbox.com", "mohmal.com", "emailondeck.com", "spamgourmet.com",
    };

    private static readonly HashSet<string> FreeMail = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "icloud.com",
        "me.com", "aol.com", "proton.me", "protonmail.com", "gmx.com", "mail.com", "zoho.com", "yandex.com",
    };

    public static IReadOnlyList<string> Validate(SignupInput input)
    {
        var errors = new List<string>();
        var company = input.CompanyName?.Trim() ?? "";
        if (company.Length is < 2 or > 120)
        {
            errors.Add("invalid_company");
        }

        var email = NormalizeEmail(input.Email);
        if (!EmailPattern().IsMatch(email))
        {
            errors.Add("invalid_email");
        }
        else
        {
            var domain = DomainOf(email);
            if (IsDisposable(domain))
            {
                errors.Add("disposable_email");
            }
            else if (FreeMail.Contains(domain))
            {
                errors.Add("work_email_required");
            }
        }

        if (input.Region is not ("US" or "EU"))
        {
            errors.Add("invalid_region");
        }

        if (input.PasswordRequired && !StrongPassword(input.Password))
        {
            errors.Add("weak_password");
        }

        return errors;
    }

    public static bool StrongPassword(string? password) =>
        password is not null
        && password.Length >= MinimumPasswordLength
        && password.Length <= 256
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    public static bool IsDisposable(string domain)
    {
        var current = domain.Trim().ToLowerInvariant();
        while (current.Contains('.', StringComparison.Ordinal))
        {
            if (Disposable.Contains(current))
            {
                return true;
            }

            current = current[(current.IndexOf('.', StringComparison.Ordinal) + 1)..];
        }

        return false;
    }

    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? "" : email[(at + 1)..].Trim().ToLowerInvariant();
    }

    public static string DisplayNameFor(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? email
            : string.Join(' ', parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    public static string SlugFor(string company)
    {
        var lowered = company.Trim().ToLowerInvariant();
        var slug = NonSlug().Replace(lowered, "-").Trim('-');
        if (slug.Length == 0 || !char.IsAsciiLetterLower(slug[0]))
        {
            slug = "org-" + slug;
        }

        if (slug.Length > 48)
        {
            slug = slug[..48].TrimEnd('-');
        }

        return slug.Length < 2 ? slug + "-co" : slug;
    }

    [GeneratedRegex("^[^@\\s]{1,64}@[a-z0-9.-]+\\.[a-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();
}

public static class OneTimeTokens
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""))).ToLowerInvariant();

    public static bool Matches(string token, string storedHash)
    {
        var computed = Encoding.ASCII.GetBytes(Hash(token));
        var stored = Encoding.ASCII.GetBytes(storedHash ?? "");
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt;
}

public sealed record PlanEntitlements(
    string Plan,
    int? Frameworks,
    int? Seats,
    int? Integrations,
    bool TrustCenter,
    bool ApiAccess,
    int? MonthlyPriceUsd);

public sealed record GateDecision(string Feature, bool Allowed, int Used, int? Limit, bool OverLimit, string? UpgradeTo);

public static class PlanFeatures
{
    public const string Frameworks = "frameworks";

    public const string Seats = "seats";

    public const string Integrations = "integrations";

    public const string TrustCenter = "trust_center";

    public const string ApiAccess = "api_access";

    public static IReadOnlyList<string> All { get; } = [Frameworks, Seats, Integrations, TrustCenter, ApiAccess];
}

public static class PlanCatalog
{
    public const string Starter = "Starter";

    public const string Growth = "Growth";

    public const string Enterprise = "Enterprise";

    public static IReadOnlyList<PlanEntitlements> All { get; } =
    [
        new(Starter, 1, 10, 3, false, false, 399),
        new(Growth, 4, 50, 15, true, true, 1199),
        new(Enterprise, null, null, null, true, true, null),
    ];

    public static bool IsKnown(string? plan) => All.Any(item => item.Plan == plan);

    public static PlanEntitlements For(string? plan) => All.FirstOrDefault(item => item.Plan == plan) ?? All[0];

    public static string? NextAbove(string plan) => plan switch
    {
        Starter => Growth,
        Growth => Enterprise,
        _ => null,
    };
}

public static class FeatureGate
{
    public static GateDecision Check(PlanEntitlements plan, string feature, int used = 0)
    {
        switch (feature)
        {
            case PlanFeatures.TrustCenter:
                return Flag(plan, feature, plan.TrustCenter);
            case PlanFeatures.ApiAccess:
                return Flag(plan, feature, plan.ApiAccess);
        }

        int? limit = feature switch
        {
            PlanFeatures.Frameworks => plan.Frameworks,
            PlanFeatures.Seats => plan.Seats,
            PlanFeatures.Integrations => plan.Integrations,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown feature."),
        };
        var over = limit is int max && used > max;
        var atOrOver = limit is int cap && used >= cap;
        return new GateDecision(feature, !atOrOver, used, limit, over, atOrOver ? UpgradeFor(plan, feature, used) : null);
    }

    private static GateDecision Flag(PlanEntitlements plan, string feature, bool enabled) =>
        new(feature, enabled, 0, null, false, enabled ? null : UpgradeFor(plan, feature, 0));

    private static string? UpgradeFor(PlanEntitlements plan, string feature, int used)
    {
        var next = PlanCatalog.NextAbove(plan.Plan);
        while (next is not null)
        {
            var candidate = PlanCatalog.For(next);
            var fits = feature switch
            {
                PlanFeatures.TrustCenter => candidate.TrustCenter,
                PlanFeatures.ApiAccess => candidate.ApiAccess,
                PlanFeatures.Frameworks => candidate.Frameworks is null || candidate.Frameworks > used,
                PlanFeatures.Seats => candidate.Seats is null || candidate.Seats > used,
                PlanFeatures.Integrations => candidate.Integrations is null || candidate.Integrations > used,
                _ => true,
            };
            if (fits)
            {
                return next;
            }

            next = PlanCatalog.NextAbove(next);
        }

        return null;
    }
}

public sealed record CompanyProfile(
    string CompanySize,
    string Industry,
    IReadOnlyList<string> CloudProviders,
    IReadOnlyList<string> DataTypes,
    bool International = false,
    bool UsesAi = false);

public sealed record FrameworkRecommendation(string FrameworkKey, string Reason);

public sealed record RecommendationResult(IReadOnlyList<FrameworkRecommendation> Frameworks, IReadOnlyList<string> ScopedControls);

public static class FrameworkRecommender
{
    public static IReadOnlyList<string> Industries { get; } =
        ["saas", "fintech", "healthcare", "ecommerce", "manufacturing", "public_sector", "ai", "other"];

    public static IReadOnlyList<string> Sizes { get; } = ["1-10", "11-50", "51-200", "201-1000", "1000+"];

    public static IReadOnlyList<string> DataTypeOptions { get; } =
        ["customer_pii", "eu_personal_data", "health", "payment_cards", "financial", "ai_training", "ot_telemetry"];

    public static IReadOnlyList<string> CloudOptions { get; } = ["aws", "azure", "gcp", "on_prem"];

    private static readonly string[] BaseControls = ["GV-01", "GV-02", "RM-01", "AC-04", "AC-07", "AC-12", "HR-01", "HR-08", "IR-02", "LG-03", "VM-02", "BC-04"];

    public static RecommendationResult Recommend(CompanyProfile profile)
    {
        var data = profile.DataTypes.ToHashSet(StringComparer.Ordinal);
        var clouds = profile.CloudProviders.Where(item => item != "on_prem").ToArray();
        var picks = new List<FrameworkRecommendation> { new("soc2", "customer_trust") };
        var controls = new SortedSet<string>(BaseControls, StringComparer.Ordinal);

        void Add(string key, string reason, params string[] scoped)
        {
            if (picks.All(item => item.FrameworkKey != key))
            {
                picks.Add(new FrameworkRecommendation(key, reason));
            }

            foreach (var code in scoped)
            {
                controls.Add(code);
            }
        }

        var larger = profile.CompanySize is "51-200" or "201-1000" or "1000+";
        if (larger || profile.International)
        {
            Add("iso27001", "international_or_scale", "GV-03", "GV-04", "AC-15");
            if (clouds.Length > 0)
            {
                Add("iso27017", "cloud_services", "CFG-01", "CR-02");
            }

            if (data.Contains("customer_pii") || data.Contains("eu_personal_data"))
            {
                Add("iso27018", "cloud_pii", "PP-01", "CR-05");
            }
        }

        if (data.Contains("health") || profile.Industry == "healthcare")
        {
            Add("hipaa", "health_data", "CR-02", "LG-08", "PP-01");
        }

        if (data.Contains("payment_cards"))
        {
            Add("pci-dss", "payment_cards", "CR-02", "VM-05", "CFG-01", "MAL-01");
        }

        if (data.Contains("eu_personal_data"))
        {
            Add("gdpr", "eu_personal_data", "PP-01", "CR-05");
        }

        if (profile.Industry == "fintech" && (data.Contains("eu_personal_data") || profile.International))
        {
            Add("dora", "eu_financial_services", "BC-01", "IR-02", "GV-04");
        }

        if (profile.UsesAi || profile.Industry == "ai" || data.Contains("ai_training"))
        {
            Add("iso42001", "ai_management", "GV-03");
            Add("nist-ai-rmf", "ai_risk", "RM-01");
            if (data.Contains("eu_personal_data") || profile.International)
            {
                Add("eu-ai-act", "eu_ai_market");
            }
        }

        if (profile.Industry == "manufacturing" || data.Contains("ot_telemetry"))
        {
            Add("iec62443", "operational_technology", "CFG-01", "PH-02", "EP-08");
        }

        if (profile.Industry == "public_sector" || picks.Count == 1)
        {
            Add("nist-csf", "baseline", "CM-11", "EP-03");
        }

        if (clouds.Length > 0)
        {
            controls.Add("CFG-01");
        }

        return new RecommendationResult(picks, controls.ToArray());
    }
}

public sealed record ReadinessTask(string Title, DateOnly DueOn);

public sealed record ReadinessMilestone(string Key, string Title, DateOnly DueOn, IReadOnlyList<ReadinessTask> Tasks);

public static class ReadinessPlan
{
    public const int MinimumDays = 30;

    private static readonly (string Key, string Title, double At, string[] Tasks)[] Template =
    [
        ("scope", "Scope and kickoff", 0.05, ["Confirm audit scope and systems", "Name control owners"]),
        ("policies", "Policies approved", 0.25, ["Install policy templates", "Collect policy approvals and acknowledgements"]),
        ("controls", "Controls implemented", 0.5, ["Close open control gaps", "Connect integrations for automated tests"]),
        ("evidence", "Evidence collected", 0.75, ["Collect evidence for every in-scope control", "Run a full test sweep"]),
        ("review", "Internal readiness review", 0.9, ["Review open findings with owners", "Prepare the auditor request list"]),
        ("fieldwork", "Auditor fieldwork", 1.0, ["Share the audit workspace with the auditor", "Start fieldwork"]),
    ];

    public static bool TooSoon(DateOnly today, DateOnly target) => target.DayNumber - today.DayNumber < MinimumDays;

    public static IReadOnlyList<ReadinessMilestone> Generate(DateOnly today, DateOnly target, IReadOnlyList<string> frameworks)
    {
        if (TooSoon(today, target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), "target_too_soon");
        }

        var span = target.DayNumber - today.DayNumber;
        var label = frameworks.Count == 0 ? "" : " (" + string.Join(", ", frameworks.Select(key => key.ToUpperInvariant())) + ")";
        return Template.Select(step =>
        {
            var due = today.AddDays((int)Math.Round(span * step.At, MidpointRounding.AwayFromZero));
            var tasks = step.Tasks.Select((title, index) =>
            {
                var taskDue = due.AddDays(-3 * (step.Tasks.Length - 1 - index));
                return new ReadinessTask(title + (step.Key == "scope" && index == 0 ? label : ""), taskDue < today ? today : taskDue);
            }).ToArray();
            return new ReadinessMilestone(step.Key, step.Title, due, tasks);
        }).ToArray();
    }
}

public static class OnboardingSteps
{
    public static IReadOnlyList<string> All { get; } = ["profile", "frameworks", "integrations", "team", "policies", "plan"];

    public static IReadOnlyList<string> Parse(string stored) =>
        stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => All.Contains(item, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static string Join(IEnumerable<string> values) =>
        string.Join(',', values.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal));
}

public static class StripeSignature
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static string Compute(string payload, long timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string payload, string? header, string secret, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2)
            {
                continue;
            }

            if (pieces[0] == "t" && long.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                timestamp = parsed;
            }
            else if (pieces[0] == "v1")
            {
                signatures.Add(pieces[1]);
            }
        }

        if (timestamp is not long t || signatures.Count == 0)
        {
            return false;
        }

        var age = now.ToUnixTimeSeconds() - t;
        if (Math.Abs(age) > (tolerance ?? DefaultTolerance).TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(payload, t, secret));
        return signatures.Any(signature => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature)));
    }
}
