using D2DAudit.Domain;

namespace D2DAudit.Application;

public enum IdentityMode
{
    Local,
    WorkOs,
}

public sealed record IdentityPrincipal(string Subject, string Email, string DisplayName);

public sealed record AccessTokenIdentity(string Subject, DateTimeOffset ExpiresAt);

public sealed record MfaFactor(string Id, string Type);

public sealed record IdentityLoginResult
{
    public bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public IdentityPrincipal? Principal { get; init; }

    public bool MfaRequired { get; init; }

    public string? PendingAuthenticationToken { get; init; }

    public string? ChallengeId { get; init; }

    public IReadOnlyList<MfaFactor> Factors { get; init; } = [];

    public static IdentityLoginResult Fail(string errorCode) => new() { ErrorCode = errorCode };

    public static IdentityLoginResult Mfa(
        string pendingToken,
        string? challengeId,
        IReadOnlyList<MfaFactor> factors) =>
        new()
        {
            MfaRequired = true,
            PendingAuthenticationToken = pendingToken,
            ChallengeId = challengeId,
            Factors = factors,
        };

    public static IdentityLoginResult Success(
        string accessToken,
        string? refreshToken,
        DateTimeOffset expiresAt,
        IdentityPrincipal principal) =>
        new()
        {
            Succeeded = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            Principal = principal,
        };
}

public sealed record AuthorizationRedirect(bool Ok, string? Url, string? ErrorCode);

public sealed record ExternalLoginStart(string Provider, string RedirectUri, string State, string? Email, string? OrganizationId);

public interface IIdentityProvider
{
    IdentityMode Mode { get; }

    Task<IdentityLoginResult> PasswordLoginAsync(string email, string password, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<IdentityLoginResult> CompleteMfaAsync(
        string pendingToken,
        string code,
        string? challengeId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<AuthorizationRedirect> GetAuthorizationUrlAsync(ExternalLoginStart start, CancellationToken cancellationToken);

    Task<IdentityLoginResult> CompleteAuthorizationCodeAsync(
        string code,
        string redirectUri,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken);

    Task<IdentityLoginResult> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<AccessTokenIdentity?> ValidateAccessTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>True when the provider accepted the request. False when this mode has no reset.</summary>
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken);
}

public static class IdentityModeResolver
{
    public static IdentityMode Resolve(string environmentName, string? apiKey, string? clientId)
    {
        var keySet = !string.IsNullOrWhiteSpace(apiKey);
        var idSet = !string.IsNullOrWhiteSpace(clientId);
        if (keySet != idSet)
        {
            throw new InvalidOperationException(
                "Set both WORKOS_API_KEY and WORKOS_CLIENT_ID, or leave both unset for local development.");
        }

        if (keySet)
        {
            return IdentityMode.WorkOs;
        }

        if (environmentName is "Development" or "Testing")
        {
            return IdentityMode.Local;
        }

        throw new InvalidOperationException(
            "Production requires WORKOS_API_KEY and WORKOS_CLIENT_ID. Local identity mode is disabled outside Development and Testing.");
    }
}

public sealed class TenantContext
{
    public bool IsResolved { get; private set; }

    public Guid TenantId { get; private set; }

    public string TenantName { get; private set; } = "";

    public string Region { get; private set; } = "";

    public bool IsExample { get; private set; }

    public Guid UserId { get; private set; }

    public string Email { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public TenantRole Role { get; private set; }

    public IReadOnlyList<string> AuditorFrameworkKeys { get; private set; } = [];

    public string DatabaseName { get; private set; } = "";

    public bool IsAuditor => Role == TenantRole.Auditor;

    public bool CanWrite => Role is TenantRole.Admin or TenantRole.ControlOwner or TenantRole.Editor;

    public bool IsAdmin => Role == TenantRole.Admin;

    public void Resolve(
        Guid tenantId,
        string tenantName,
        string region,
        bool isExample,
        Guid userId,
        string email,
        string displayName,
        TenantRole role,
        IReadOnlyList<string> auditorFrameworkKeys,
        string databaseName)
    {
        if (!DatabaseNames.IsSafe(databaseName))
        {
            throw new InvalidOperationException("Tenant database name failed the safety check.");
        }

        IsResolved = true;
        TenantId = tenantId;
        TenantName = tenantName;
        Region = region;
        IsExample = isExample;
        UserId = userId;
        Email = email;
        DisplayName = displayName;
        Role = role;
        AuditorFrameworkKeys = auditorFrameworkKeys;
        DatabaseName = databaseName;
    }
}

public interface ITenantDatabase
{
    TenantContext Tenant { get; }
}

public sealed record CreateTenantRequest(string Slug, string Name, string Region, bool IsExample);

public interface ITenantProvisioner
{
    Task<Result<Tenant>> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken);

    Task MigrateExistingAsync(Guid tenantId, CancellationToken cancellationToken);

    Task SeedExampleControlsAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IDemoSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

public sealed record DirectoryUser(Guid Id, string Email, string DisplayName);

public sealed record DirectoryMembership(
    Guid TenantId,
    string TenantName,
    string Region,
    bool IsExample,
    TenantStatus Status,
    TenantRole Role,
    string? DatabaseName);

public sealed record DirectoryGrant(Guid TenantId, IReadOnlyList<string> FrameworkKeys);

public interface IControlPlaneDirectory
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);

    Task<DirectoryUser?> FindUserBySubjectAsync(string subject, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryMembership>> MembershipsAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryGrant>> ActiveGrantsAsync(Guid userId, DateOnly today, CancellationToken cancellationToken);
}

public interface IActivityWriter
{
    Task WriteAsync(
        Guid? actorUserId,
        Guid? tenantId,
        string action,
        string result,
        string? detail,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public interface IJobNotifier
{
    Task JobChangedAsync(JobRecord job, CancellationToken cancellationToken);
}

public sealed record UserSummary(Guid Id, string Email, string DisplayName);

public sealed record TenantMembershipSummary(
    Guid TenantId,
    string Name,
    string Slug,
    string Region,
    string Role,
    bool ExampleData);

public sealed record AuthSession(
    bool Ok,
    string? ErrorCode,
    UserSummary? User,
    IReadOnlyList<TenantMembershipSummary>? Tenants,
    bool MfaRequired,
    string? PendingAuthenticationToken,
    string? ChallengeId,
    IReadOnlyList<MfaFactor>? Factors,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);

public sealed record AuthModeResponse(string Mode, bool LocalBanner, IReadOnlyList<string> ExampleEmails);

public interface IAuthService
{
    Task<AuthSession> PasswordLoginAsync(string email, string password, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<AuthSession> CompleteMfaAsync(string pendingToken, string code, string? challengeId, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<AuthorizationRedirect> StartExternalAsync(ExternalLoginStart start, CancellationToken cancellationToken);

    Task<AuthSession> CompleteCodeAsync(string code, string redirectUri, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<AuthSession> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken);

    Task<string> RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task<AuthSession?> CurrentAsync(string subject, CancellationToken cancellationToken);

    AuthModeResponse Mode();
}

public sealed record WorkspaceResponse(
    Guid TenantId,
    string Name,
    string Region,
    string Role,
    bool ExampleData,
    bool CanWrite,
    string DisplayName,
    string Email,
    int FailingControls);

public sealed record FrameworkReadiness(string Key, string Name, int Passing, int Total, int Percent);

public sealed record AuditSummary(
    string Name,
    string Firm,
    string FrameworkKey,
    DateOnly FieldworkOn,
    int OpenItems,
    int SamplesDue);

public sealed record FailingTestRow(
    Guid TestId,
    string TestName,
    string ControlCode,
    string? Detail,
    IReadOnlyList<string> RequirementCodes);

public sealed record ControlHealth(int Passing, int Failing, int DueSoon, int NeedsEvidence, int Manual, int NotStarted);

public sealed record DashboardResponse(
    string DisplayName,
    string TenantName,
    string Region,
    bool ExampleData,
    int ReadinessPercent,
    int FrameworkCount,
    IReadOnlyList<FrameworkReadiness> Frameworks,
    AuditSummary? Audit,
    IReadOnlyList<FailingTestRow> FailingTests,
    ControlHealth Health,
    IReadOnlyList<DueItem> DueItems,
    IReadOnlyList<HistoryPoint> History,
    int DevicesReporting);

public sealed record HistoryPoint(DateOnly WeekOf, int Passing, int Failing);

public sealed record FrameworkListItem(
    string Key,
    string Name,
    string Version,
    string Description,
    int RequirementCount,
    int MappedControlCount,
    int PassingControlCount,
    int Percent);

public sealed record MappedControlDto(string Code, string Title, string Coverage, string Status);

public sealed record RequirementNodeDto(
    string Code,
    string Title,
    string Summary,
    IReadOnlyList<MappedControlDto> Controls,
    IReadOnlyList<RequirementNodeDto> Children);

public sealed record FrameworkDetail(
    string Key,
    string Name,
    string Version,
    string Description,
    IReadOnlyList<RequirementNodeDto> Requirements);

public sealed record ControlListItem(
    string Code,
    string Title,
    string Status,
    string? OwnerName,
    string? OwnerEmail,
    string Frequency,
    int TestsPassing,
    int TestsTotal,
    IReadOnlyList<string> Frameworks);

public sealed record ControlMappingDto(
    string FrameworkKey,
    string FrameworkName,
    string RequirementCode,
    string RequirementTitle,
    string Coverage);

public sealed record ControlTestDto(Guid Id, string Name, string Kind, string Outcome, string? Detail, string? Remediation);

public sealed record EvidenceDto(
    Guid Id,
    string Title,
    string Source,
    DateTimeOffset? CollectedAt,
    string Integrity,
    string? Sha256,
    DateTimeOffset? LockedUntil);

public sealed record ControlDetail(
    string Code,
    string Title,
    string Description,
    string Status,
    string? OwnerName,
    string? OwnerEmail,
    string Frequency,
    IReadOnlyList<ControlMappingDto> Mappings,
    IReadOnlyList<ControlTestDto> Tests,
    IReadOnlyList<EvidenceDto> Evidence);

public sealed record AssignOwnerRequest(string? OwnerName, string? OwnerEmail);

public sealed record JobDto(
    Guid Id,
    string Kind,
    string Status,
    int Step,
    int TotalSteps,
    string CurrentStep,
    int? Percent,
    string? Error,
    DateTimeOffset UpdatedAt);

public sealed record ActivityDto(DateTimeOffset At, string Action, string Result, string? Detail, string? ActorEmail, string? IpAddress);

public interface IWorkspaceQuery
{
    Task<WorkspaceResponse> GetAsync(CancellationToken cancellationToken);

    Task<DashboardResponse> DashboardAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FrameworkListItem>> FrameworksAsync(CancellationToken cancellationToken);

    Task<FrameworkDetail?> FrameworkAsync(string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<ControlListItem>> ControlsAsync(string? status, string? query, string? ownerEmail, CancellationToken cancellationToken);

    Task<ControlDetail?> ControlAsync(string code, CancellationToken cancellationToken);

    Task<Result<ControlDetail>> AssignOwnerAsync(string code, AssignOwnerRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobDto>> JobsAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ActivityDto>>> ActivityAsync(CancellationToken cancellationToken);
}

public sealed class RequirementNode
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public List<RequirementNode> Children { get; set; } = [];
}

public sealed class FrameworkDocument
{
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Version { get; set; } = "";

    public string Description { get; set; } = "";

    public List<RequirementNode> Requirements { get; set; } = [];
}

public sealed class ExampleTestDocument
{
    public string Name { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string? Detail { get; set; }
}

public sealed class ExampleMappingDocument
{
    public string Framework { get; set; } = "";

    public string Requirement { get; set; } = "";

    public string Coverage { get; set; } = "";
}

public sealed class ExampleControlDocument
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string Status { get; set; } = "";

    public string Frequency { get; set; } = "";

    public string? OwnerName { get; set; }

    public string? OwnerEmail { get; set; }

    public List<ExampleMappingDocument> Mappings { get; set; } = [];

    public List<ExampleTestDocument> Tests { get; set; } = [];
}

public sealed class ExampleCatalogDocument
{
    public List<ExampleControlDocument> Controls { get; set; } = [];
}

public interface IFrameworkCatalog
{
    IReadOnlyList<FrameworkDocument> Frameworks { get; }

    ExampleCatalogDocument ExampleControls { get; }
}
