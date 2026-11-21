using D2DAudit.Api.Security;
using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Api.Endpoints;

public static class OnboardingEndpoints
{
    public const string BillingWebhookPath = "/api/v1/billing/webhook";

    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var signup = app.MapGroup("/api/v1/signup").RequireRateLimiting("signup");
        signup.MapGet("/options", (ISignupService service) => Results.Ok(service.Options())).Produces<SignupOptionsDto>();
        signup.MapPost("", StartSignup).Produces<SignupAccepted>();
        signup.MapPost("/verify", VerifySignup).Produces<SignupStatusDto>();
        signup.MapGet("/{id:guid}", async (Guid id, ISignupService service, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await service.StatusAsync(id, cancellationToken))).Produces<SignupStatusDto>();

        var invites = app.MapGroup("/api/v1/invites").RequireRateLimiting("signup");
        invites.MapGet("/preview", async (string token, IInviteAcceptance service, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await service.PreviewAsync(token, cancellationToken))).Produces<InvitePreviewDto>();
        invites.MapPost("/accept", AcceptInvite).Produces<AcceptInviteResult>();

        app.MapPost(BillingWebhookPath, BillingWebhook).RequireRateLimiting("public_trust").ExcludeFromDescription();

        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/onboarding", (IOnboarding portal, CancellationToken cancellationToken) => portal.GetAsync(cancellationToken)).Produces<OnboardingStateDto>();
        group.MapPut("/onboarding", SaveOnboarding).RequireAuthorization("TenantWrite").Produces<OnboardingStateDto>();
        group.MapPost("/onboarding/recommend", (RecommendFrameworksRequest body, IOnboarding portal) => Results.Ok(portal.Recommend(body))).Produces<RecommendationResult>();
        group.MapPost("/onboarding/plan", GeneratePlan).RequireAuthorization("TenantWrite").Produces<ReadinessPlanDto>();
        group.MapPost("/onboarding/dismiss", (IOnboarding portal, CancellationToken cancellationToken) => portal.DismissAsync(cancellationToken)).RequireAuthorization("TenantWrite").Produces<OnboardingStateDto>();

        group.MapGet("/billing", (IBilling portal, CancellationToken cancellationToken) => portal.OverviewAsync(cancellationToken)).Produces<BillingOverviewDto>();
        group.MapPost("/billing/plan", ChangePlan).RequireAuthorization("TenantAdmin").Produces<ChangePlanResult>();
        group.MapPost("/billing/portal", async (IBilling portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.PortalAsync(cancellationToken))).RequireAuthorization("TenantAdmin").Produces<BillingPortalResult>();
        group.MapGet("/billing/entitlements/{feature}", CheckFeature).Produces<GateDecision>();

        group.MapGet("/settings/invites", (ITeamAccess portal, CancellationToken cancellationToken) => portal.InvitesAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<InviteDto>>();
        group.MapPost("/settings/invites", CreateInvite).RequireAuthorization("TenantAdmin").Produces<CreatedInviteDto>();
        group.MapPost("/settings/invites/{id:guid}/revoke", async (Guid id, ITeamAccess portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RevokeInviteAsync(id, cancellationToken))).RequireAuthorization("TenantAdmin").Produces<InviteDto>();
        group.MapGet("/settings/security", (ITeamAccess portal, CancellationToken cancellationToken) => portal.SecurityAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<SecuritySettingsDto>();
        group.MapPut("/settings/security", SaveSecurity).RequireAuthorization("TenantAdmin").Produces<SecuritySettingsDto>();
        group.MapPost("/settings/security/scim-token", (ITeamAccess portal, CancellationToken cancellationToken) => portal.RotateScimTokenAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<ScimTokenDto>();

        group.MapGet("/risks/heatmap", (IRiskInsights portal, CancellationToken cancellationToken) => portal.HeatmapAsync(cancellationToken)).Produces<RiskHeatmapResult>();
        group.MapPost("/risks/{code}/accept", AcceptRisk).RequireAuthorization("TenantWrite").Produces<RiskListItem>();
        return app;
    }

    private static async Task<IResult> StartSignup(SignupRequest body, ISignupService service, HttpContext http, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await service.StartAsync(body, Ip(http), cancellationToken));

    private static async Task<IResult> VerifySignup(VerifySignupRequest body, ISignupService service, HttpContext http, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await service.VerifyAsync(body, Ip(http), cancellationToken));

    private static async Task<IResult> AcceptInvite(AcceptInviteRequest body, IInviteAcceptance service, HttpContext http, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await service.AcceptAsync(body, Ip(http), cancellationToken));

    private static async Task<IResult> BillingWebhook(HttpContext http, IBillingWebhooks webhooks, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(http.Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var result = await webhooks.HandleAsync(payload, http.Request.Headers["Stripe-Signature"].ToString(), cancellationToken);
        return result.IsSuccess ? Results.Ok(new { status = result.Value }) : EndpointResults.FromResult(result);
    }

    private static async Task<IResult> SaveOnboarding(SaveOnboardingRequest body, IOnboarding portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveAsync(body, cancellationToken));

    private static async Task<IResult> GeneratePlan(GenerateReadinessPlanRequest body, IOnboarding portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.GeneratePlanAsync(body, cancellationToken));

    private static async Task<IResult> ChangePlan(ChangePlanRequest body, IBilling portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ChangePlanAsync(body, cancellationToken));

    private static async Task<IResult> CheckFeature(string feature, IFeatureGate gate, TenantContext tenant, CancellationToken cancellationToken) =>
        PlanFeatures.All.Contains(feature)
            ? Results.Ok(await gate.CheckAsync(tenant.TenantId, feature, cancellationToken))
            : Results.NotFound(new { code = "not_found" });

    private static async Task<IResult> CreateInvite(CreateInviteRequest body, ITeamAccess portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateInviteAsync(body, cancellationToken));

    private static async Task<IResult> SaveSecurity(SaveSecuritySettingsRequest body, ITeamAccess portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveSecurityAsync(body, cancellationToken));

    private static async Task<IResult> AcceptRisk(string code, AcceptRiskRequest body, IRiskInsights portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AcceptAsync(code, body, cancellationToken));

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
