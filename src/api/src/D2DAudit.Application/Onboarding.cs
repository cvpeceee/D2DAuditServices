using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record SignupOptionsDto(string IdentityMode, bool PasswordRequired, IReadOnlyList<string> Regions, int MinimumPasswordLength);

public sealed record SignupRequest(string CompanyName, string Email, string? Password, string Region, string? FullName = null);

public sealed record SignupAccepted(Guid SignupId, string Status, string Email, DateTimeOffset ExpiresAt, string? DevVerificationUrl);

public sealed record VerifySignupRequest(string Token);

public sealed record SignupStatusDto(
    Guid SignupId,
    string Status,
    string Email,
    Guid? TenantId,
    string CompanyName,
    int Percent,
    string CurrentStep,
    string? Error,
    string NextAction);

public interface ISignupService
{
    SignupOptionsDto Options();

    Task<Result<SignupAccepted>> StartAsync(SignupRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<Result<SignupStatusDto>> VerifyAsync(VerifySignupRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<Result<SignupStatusDto>> StatusAsync(Guid signupId, CancellationToken cancellationToken);
}

public sealed record SignupProvisionWork(Guid SignupId);

public interface ISignupProvisioningQueue
{
    ValueTask EnqueueAsync(SignupProvisionWork work, CancellationToken cancellationToken);
}

public sealed record OnboardingOptionsDto(
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> CloudProviders,
    IReadOnlyList<string> DataTypes,
    IReadOnlyList<string> Frameworks);

public sealed record OnboardingStateDto(
    string CompanySize,
    string Industry,
    IReadOnlyList<string> CloudProviders,
    IReadOnlyList<string> DataTypes,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> ScopedControls,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> CompletedSteps,
    DateOnly? AuditTargetDate,
    bool Dismissed,
    bool Complete,
    int Percent,
    OnboardingOptionsDto Options);

public sealed record SaveOnboardingRequest(
    string? CompanySize = null,
    string? Industry = null,
    IReadOnlyList<string>? CloudProviders = null,
    IReadOnlyList<string>? DataTypes = null,
    IReadOnlyList<string>? Frameworks = null,
    IReadOnlyList<string>? ScopedControls = null,
    string? CompleteStep = null);

public sealed record RecommendFrameworksRequest(
    string CompanySize,
    string Industry,
    IReadOnlyList<string> CloudProviders,
    IReadOnlyList<string> DataTypes,
    bool International = false,
    bool UsesAi = false);

public sealed record GenerateReadinessPlanRequest(DateOnly TargetDate);

public sealed record ReadinessPlanDto(DateOnly TargetDate, IReadOnlyList<ReadinessMilestone> Milestones, int TasksCreated, GateDecision Frameworks);

public interface IOnboarding
{
    Task<OnboardingStateDto> GetAsync(CancellationToken cancellationToken);

    Task<Result<OnboardingStateDto>> SaveAsync(SaveOnboardingRequest request, CancellationToken cancellationToken);

    RecommendationResult Recommend(RecommendFrameworksRequest request);

    Task<Result<ReadinessPlanDto>> GeneratePlanAsync(GenerateReadinessPlanRequest request, CancellationToken cancellationToken);

    Task<OnboardingStateDto> DismissAsync(CancellationToken cancellationToken);
}

public sealed record InvoiceDto(string Id, string Number, decimal AmountUsd, string Status, DateOnly IssuedOn, string? Url);

public sealed record BillingOverviewDto(
    string Plan,
    string Status,
    string Provider,
    bool ProviderConfigured,
    PlanEntitlements Entitlements,
    IReadOnlyList<GateDecision> Usage,
    IReadOnlyList<PlanEntitlements> Plans,
    IReadOnlyList<InvoiceDto> Invoices,
    DateOnly? CurrentPeriodEnd);

public sealed record ChangePlanRequest(string Plan);

public sealed record ChangePlanResult(string Plan, string Status, string? RedirectUrl);

public sealed record BillingPortalResult(string Url);

public sealed record BillingCheckout(Guid TenantId, string Plan, string Email, string? CustomerId, string SuccessUrl, string CancelUrl);

public sealed record BillingRedirect(bool Immediate, string? Url);

public sealed record BillingWebhookEvent(string Id, string Type, Guid? TenantId, string? Plan, string? CustomerId, string? SubscriptionId, string? Status);

/// <summary>Payment provider. Mock by default; Stripe when STRIPE_SECRET_KEY is set.</summary>
public interface IBillingProvider
{
    string Name { get; }

    bool Configured { get; }

    Task<Result<BillingRedirect>> CreateCheckoutAsync(BillingCheckout checkout, CancellationToken cancellationToken);

    Task<Result<BillingPortalResult>> CreatePortalAsync(string? customerId, string returnUrl, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceDto>> InvoicesAsync(Guid tenantId, string plan, string? customerId, DateOnly today, CancellationToken cancellationToken);

    BillingWebhookEvent? ParseWebhook(string payload, string? signatureHeader, DateTimeOffset now);
}

public interface IBilling
{
    Task<BillingOverviewDto> OverviewAsync(CancellationToken cancellationToken);

    Task<Result<ChangePlanResult>> ChangePlanAsync(ChangePlanRequest request, CancellationToken cancellationToken);

    Task<Result<BillingPortalResult>> PortalAsync(CancellationToken cancellationToken);
}

public interface IBillingWebhooks
{
    Task<Result<string>> HandleAsync(string payload, string? signatureHeader, CancellationToken cancellationToken);
}

public interface IFeatureGate
{
    Task<PlanEntitlements> EntitlementsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<GateDecision> CheckAsync(Guid tenantId, string feature, CancellationToken cancellationToken);

    Task<IReadOnlyList<GateDecision>> UsageAsync(Guid tenantId, string? databaseName, CancellationToken cancellationToken);
}

public sealed record InviteDto(Guid Id, string Email, string Role, string State, string InvitedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record CreateInviteRequest(string Email, string Role);

public sealed record CreatedInviteDto(InviteDto Invite, string? DevAcceptUrl, GateDecision Seats);

public sealed record InvitePreviewDto(string TenantName, string Email, string Role, string State, bool PasswordRequired, DateTimeOffset ExpiresAt);

public sealed record AcceptInviteRequest(string Token, string? DisplayName, string? Password);

public sealed record AcceptInviteResult(Guid TenantId, string Email, string NextAction);

public sealed record SecuritySettingsDto(
    bool SsoEnforced,
    string Mode,
    string Domain,
    bool Connected,
    string? ScimTokenPrefix,
    DateTimeOffset? ScimTokenCreatedAt,
    string ScimEndpoint);

public sealed record SaveSecuritySettingsRequest(bool SsoEnforced);

public sealed record ScimTokenDto(string Token, string Prefix, DateTimeOffset CreatedAt);

public interface ITeamAccess
{
    Task<IReadOnlyList<InviteDto>> InvitesAsync(CancellationToken cancellationToken);

    Task<Result<CreatedInviteDto>> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken);

    Task<Result<InviteDto>> RevokeInviteAsync(Guid id, CancellationToken cancellationToken);

    Task<SecuritySettingsDto> SecurityAsync(CancellationToken cancellationToken);

    Task<Result<SecuritySettingsDto>> SaveSecurityAsync(SaveSecuritySettingsRequest request, CancellationToken cancellationToken);

    Task<ScimTokenDto> RotateScimTokenAsync(CancellationToken cancellationToken);
}

public interface IInviteAcceptance
{
    Task<Result<InvitePreviewDto>> PreviewAsync(string token, CancellationToken cancellationToken);

    Task<Result<AcceptInviteResult>> AcceptAsync(AcceptInviteRequest request, string? ipAddress, CancellationToken cancellationToken);
}

public interface IRiskInsights
{
    Task<RiskHeatmapResult> HeatmapAsync(CancellationToken cancellationToken);

    Task<Result<RiskListItem>> AcceptAsync(string code, AcceptRiskRequest request, CancellationToken cancellationToken);
}
