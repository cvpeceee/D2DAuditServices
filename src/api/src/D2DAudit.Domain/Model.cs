using System.Text.RegularExpressions;

namespace D2DAudit.Domain;

public enum TenantStatus
{
    Provisioning,
    Active,
    Disabled,
}

public enum TenantRole
{
    Admin,
    ControlOwner,
    Employee,
    Auditor,
}

public enum ControlStatus
{
    Passing,
    Failing,
    DueSoon,
    NeedsEvidence,
    Manual,
    NotStarted,
}

public enum MappingCoverage
{
    Full,
    Partial,
}

public enum TestKind
{
    Automated,
    Document,
    Attestation,
    ManualSampling,
}

public enum TestOutcome
{
    Pass,
    Fail,
    NotRun,
    NeedsEvidence,
}

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

public enum EvidenceIntegrity
{
    NotStored,
    Verified,
    Broken,
}

public static partial class DatabaseNames
{
    public const int MinimumRetentionYears = 3;

    public static string ForTenant(Guid tenantId)
    {
        var name = "t_" + tenantId.ToString("N");
        if (!IsSafe(name))
        {
            throw new InvalidOperationException("Refusing to use an unsafe database name.");
        }

        return name;
    }

    public static bool IsSafe(string name) => SafeName().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex SafeName();
}

public sealed class Tenant
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = "";

    public string Name { get; set; } = "";

    public string Region { get; set; } = "US";

    public TenantStatus Status { get; set; }

    public string? DatabaseName { get; set; }

    public int SchemaVersion { get; set; }

    public int RetentionYears { get; set; } = DatabaseNames.MinimumRetentionYears;

    public bool IsExample { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Membership> Memberships { get; set; } = [];

    public List<TenantDomain> Domains { get; set; } = [];

    public RuleResult SetRetentionYears(int years)
    {
        if (years < DatabaseNames.MinimumRetentionYears)
        {
            return RuleResult.Fail("below_minimum");
        }

        if (years < RetentionYears)
        {
            return RuleResult.Fail("cannot_shrink");
        }

        RetentionYears = years;
        return RuleResult.Ok();
    }
}

public readonly record struct RuleResult(bool Succeeded, string? Error)
{
    public static RuleResult Ok() => new(true, null);

    public static RuleResult Fail(string error) => new(false, error);
}

public sealed class UserAccount
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = "";

    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Membership> Memberships { get; set; } = [];
}

public sealed class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public UserAccount? User { get; set; }

    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    public TenantRole Role { get; set; }
}

public sealed class AuditorGrant
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public string AuditName { get; set; } = "";

    public string FirmName { get; set; } = "";

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateOnly AccessExpiresOn { get; set; }

    public List<AuditorGrantFramework> Frameworks { get; set; } = [];

    public bool IsActiveOn(DateOnly today) => AccessExpiresOn >= today;
}

public sealed class AuditorGrantFramework
{
    public Guid GrantId { get; set; }

    public AuditorGrant? Grant { get; set; }

    public string FrameworkKey { get; set; } = "";
}

public sealed class TenantDomain
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Domain { get; set; } = "";

    public string? WorkOsOrganizationId { get; set; }
}

public sealed class JobRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string Kind { get; set; } = "";

    public JobStatus Status { get; set; }

    public int Step { get; set; }

    public int TotalSteps { get; set; }

    public string CurrentStep { get; set; } = "";

    public int? Percent { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ActivityEvent
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? TenantId { get; set; }

    public string Action { get; set; } = "";

    public string Result { get; set; } = "";

    public string? Detail { get; set; }

    public string? IpAddress { get; set; }
}

public sealed class FrameworkRecord
{
    public Guid Id { get; set; }

    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Version { get; set; } = "";

    public string Description { get; set; } = "";

    public List<Requirement> Requirements { get; set; } = [];
}

public sealed class Requirement
{
    public Guid Id { get; set; }

    public Guid FrameworkId { get; set; }

    public FrameworkRecord? Framework { get; set; }

    public Guid? ParentId { get; set; }

    public Requirement? Parent { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public int SortOrder { get; set; }

    public List<ControlRequirementMap> Mappings { get; set; } = [];
}

public sealed class Control
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string? OwnerName { get; set; }

    public string? OwnerEmail { get; set; }

    public ControlStatus Status { get; set; }

    public string Frequency { get; set; } = "";

    public List<ControlRequirementMap> Mappings { get; set; } = [];

    public List<ControlTest> Tests { get; set; } = [];

    public List<EvidenceItem> Evidence { get; set; } = [];
}

public sealed class ControlRequirementMap
{
    public Guid ControlId { get; set; }

    public Control? Control { get; set; }

    public Guid RequirementId { get; set; }

    public Requirement? Requirement { get; set; }

    public MappingCoverage Coverage { get; set; }
}

public sealed class ControlTest
{
    public Guid Id { get; set; }

    public Guid ControlId { get; set; }

    public string Name { get; set; } = "";

    public TestKind Kind { get; set; }

    public TestOutcome Outcome { get; set; }

    public string? Detail { get; set; }

    public string? TestKey { get; set; }

    public string? Remediation { get; set; }

    public string? Requirements { get; set; }
}

public sealed class EvidenceItem
{
    public Guid Id { get; set; }

    public Guid ControlId { get; set; }

    public Guid? TestId { get; set; }

    public string Title { get; set; } = "";

    public string Source { get; set; } = "";

    public DateTimeOffset? CollectedAt { get; set; }

    public string? Sha256 { get; set; }

    public EvidenceIntegrity Integrity { get; set; } = EvidenceIntegrity.NotStored;

    public string? ObjectKey { get; set; }

    public string? PreviousHead { get; set; }

    public string? ChainHead { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }
}

public sealed class AuditEngagement
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string FirmName { get; set; } = "";

    public string FrameworkKey { get; set; } = "";

    public string Scope { get; set; } = "";

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateOnly FieldworkOn { get; set; }
}
