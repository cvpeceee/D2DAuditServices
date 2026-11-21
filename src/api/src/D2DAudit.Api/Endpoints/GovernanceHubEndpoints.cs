using D2DAudit.Api.Security;
using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Api.Endpoints;

public static class GovernanceHubEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceHubEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/governance")
            .RequireAuthorization("WorkspaceMember")
            .WithMetadata(new TenantScopedAttribute());

        group.MapGet("/overview", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.OverviewAsync(cancellationToken))
            .Produces<GovernanceHubOverviewDto>();

        group.MapGet("/entities", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListEntitiesAsync(cancellationToken))
            .Produces<EntityRollupDto>();
        group.MapPost("/entities", SaveEntity)
            .RequireAuthorization("TenantWrite")
            .Produces<LegalEntityDto>();

        group.MapPost("/checkr/sync", SyncCheckr)
            .RequireAuthorization("TenantWrite")
            .Produces<SyncIntegrationResultDto>();
        group.MapPost("/knowbe4/sync", SyncKnowBe4)
            .RequireAuthorization("TenantWrite")
            .Produces<SyncIntegrationResultDto>();
        group.MapPost("/scim/deprovision", DeprovisionScim)
            .RequireAuthorization("TenantWrite")
            .Produces<ScimDeprovisionResultDto>();

        group.MapGet("/privacy", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListPrivacyAssessmentsAsync(cancellationToken))
            .Produces<IReadOnlyList<PrivacyAssessmentDto>>();
        group.MapPost("/privacy", SavePrivacy)
            .RequireAuthorization("TenantWrite")
            .Produces<PrivacyAssessmentDto>();

        group.MapGet("/ai-models", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListAiModelsAsync(cancellationToken))
            .Produces<IReadOnlyList<AiModelDto>>();
        group.MapPost("/ai-models", SaveAiModel)
            .RequireAuthorization("TenantWrite")
            .Produces<AiModelDto>();

        group.MapGet("/phishing", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListPhishingCampaignsAsync(cancellationToken))
            .Produces<IReadOnlyList<PhishingCampaignDto>>();
        group.MapPost("/phishing", CreatePhishing)
            .RequireAuthorization("TenantWrite")
            .Produces<PhishingCampaignDto>();

        group.MapGet("/attack-surface", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListAttackSurfaceAsync(cancellationToken))
            .Produces<IReadOnlyList<AttackSurfaceAssetDto>>();
        group.MapPost("/attack-surface", SaveAttackSurface)
            .RequireAuthorization("TenantWrite")
            .Produces<AttackSurfaceAssetDto>();

        group.MapGet("/auditor-network", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListAuditorNetworkAsync(cancellationToken))
            .Produces<IReadOnlyList<AuditorFirmDto>>();
        group.MapPost("/auditor-network/{id:guid}/invite", InviteAuditorFirm)
            .RequireAuthorization("TenantWrite")
            .Produces<AuditorFirmDto>();

        group.MapGet("/remediation-prs", (IGovernanceHub hub, CancellationToken cancellationToken) => hub.ListRemediationPrsAsync(cancellationToken))
            .Produces<IReadOnlyList<RemediationPrDto>>();
        group.MapPost("/remediation-prs", CreateRemediationPr)
            .RequireAuthorization("TenantWrite")
            .Produces<RemediationPrDto>();

        group.MapGet("/time-machine", TimeMachine)
            .Produces<TimeMachineComparison>();

        var scim = app.MapGroup("/api/v1/scim/v2")
            .RequireAuthorization("TenantWrite")
            .WithMetadata(new TenantScopedAttribute());
        scim.MapPost("/Users/{email}/deprovision", DeprovisionScimByPath)
            .Produces<ScimDeprovisionResultDto>();

        return app;
    }

    private static async Task<IResult> SaveEntity(SaveEntityRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SaveEntityAsync(body, cancellationToken));

    private static async Task<IResult> SyncCheckr(IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SyncCheckrAsync(cancellationToken));

    private static async Task<IResult> SyncKnowBe4(IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SyncKnowBe4Async(cancellationToken));

    private static async Task<IResult> DeprovisionScim(ScimDeprovisionRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.DeprovisionScimUserAsync(body, cancellationToken));

    private static async Task<IResult> DeprovisionScimByPath(string email, ScimDeprovisionRequest? body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.DeprovisionScimUserAsync(
            new ScimDeprovisionRequest(
                Uri.UnescapeDataString(email),
                body?.Reason ?? "SCIM 2.0 RFC 7644 user deprovisioning",
                body?.RevokeDownstreamAccounts ?? false),
            cancellationToken));

    private static async Task<IResult> SavePrivacy(SavePrivacyAssessmentRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SavePrivacyAssessmentAsync(body, cancellationToken));

    private static async Task<IResult> SaveAiModel(SaveAiModelRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SaveAiModelAsync(body, cancellationToken));

    private static async Task<IResult> CreatePhishing(CreatePhishingCampaignRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.CreatePhishingCampaignAsync(body, cancellationToken));

    private static async Task<IResult> SaveAttackSurface(SaveAttackSurfaceAssetRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.SaveAttackSurfaceAssetAsync(body, cancellationToken));

    private static async Task<IResult> InviteAuditorFirm(Guid id, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.InviteAuditorFirmAsync(id, cancellationToken));

    private static async Task<IResult> CreateRemediationPr(CreateRemediationPrRequest body, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.CreateRemediationPrAsync(body, cancellationToken));

    private static async Task<IResult> TimeMachine(DateOnly? asOf, DateOnly? compareTo, IGovernanceHub hub, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await hub.TimeMachineAsync(asOf, compareTo, cancellationToken));
}
