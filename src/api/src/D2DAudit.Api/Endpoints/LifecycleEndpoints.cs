using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class LifecycleEndpoints
{
    public static IEndpointRouteBuilder MapLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/lifecycle").RequireAuthorization("WorkspaceMember").WithMetadata(new TenantScopedAttribute());
        group.MapGet("/overview", (ILifecycle portal, CancellationToken cancellationToken) => portal.OverviewAsync(cancellationToken)).Produces<LifecycleOverviewDto>();
        group.MapPost("/settings", async (LifecycleSettingsDto body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SaveSettingsAsync(body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleSettingsDto>();
        group.MapPost("/groups", async (SaveEmployeeGroupRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SaveGroupAsync(body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<EmployeeGroupDto>();
        group.MapPost("/templates", async (SaveLifecycleTemplateStepRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SaveTemplateStepAsync(body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleTemplateStepDto>();
        group.MapDelete("/templates/{id:guid}", async (Guid id, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.DeleteTemplateStepAsync(id, cancellationToken))).RequireAuthorization("TenantWrite").Produces<bool>();
        group.MapPost("/sync", async (ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SyncHrisAsync(cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleSyncResult>();
        group.MapGet("/people/{id:guid}", async (Guid id, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.PersonAsync(id, cancellationToken))).Produces<PersonLifecycleDto>();
        group.MapPost("/people/{id:guid}/offboard", async (Guid id, StartOffboardingRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.StartOffboardingAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleChecklistDto>();
        group.MapPost("/people/{id:guid}/onboard", async (Guid id, StartOnboardingRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.StartOnboardingAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleChecklistDto>();
        group.MapPost("/steps/{id:guid}", async (Guid id, CompleteLifecycleStepRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.CompleteStepAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleChecklistDto>();
        group.MapPost("/steps/{id:guid}/revoke", async (Guid id, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RevokeStepAsync(id, cancellationToken))).RequireAuthorization("TenantWrite").Produces<LifecycleChecklistDto>();
        group.MapPost("/checklists/{id:guid}/revoke-all", async (Guid id, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RevokeAllAsync(id, cancellationToken))).RequireAuthorization("TenantWrite").Produces<RevokeAllResult>();
        group.MapPost("/tests/refresh", async (ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RefreshTestsAsync(cancellationToken))).RequireAuthorization("TenantWrite").Produces<IReadOnlyList<LifecycleTestDto>>();

        var me = app.MapGroup("/api/v1/me/lifecycle").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        me.MapGet("", async (ILifecycle portal, CancellationToken cancellationToken) =>
        {
            var mine = await portal.MineAsync(cancellationToken);
            return mine is null ? Results.NoContent() : Results.Ok(mine);
        }).Produces<PersonLifecycleDto>();
        me.MapPost("/steps/{id:guid}", async (Guid id, CompleteLifecycleStepRequest body, ILifecycle portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.CompleteMyStepAsync(id, body, cancellationToken))).Produces<LifecycleChecklistDto>();
        return app;
    }
}
