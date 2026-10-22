using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/workspace", (IWorkspaceQuery query, CancellationToken cancellationToken) => query.GetAsync(cancellationToken));
        group.MapGet("/dashboard", (IWorkspaceQuery query, CancellationToken cancellationToken) => query.DashboardAsync(cancellationToken));
        group.MapGet("/frameworks", (IWorkspaceQuery query, CancellationToken cancellationToken) => query.FrameworksAsync(cancellationToken));
        group.MapGet("/frameworks/{key}", Framework).Produces<FrameworkDetail>();
        group.MapGet("/controls", Controls);
        group.MapGet("/controls/{code}", Control).Produces<ControlDetail>();
        group.MapPut("/controls/{code}/owner", AssignOwner).RequireAuthorization("TenantWrite").Produces<ControlDetail>();
        group.MapGet("/jobs", (IWorkspaceQuery query, CancellationToken cancellationToken) => query.JobsAsync(cancellationToken));
        group.MapGet("/activity", Activity).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<ActivityDto>>();
        group.MapGet("/notifications", (IClientRead read, CancellationToken cancellationToken) => read.NotificationsAsync(cancellationToken));
        group.MapGet("/devices", (IClientRead read, CancellationToken cancellationToken) => read.DevicesAsync(cancellationToken));
        group.MapGet("/resources", (IClientRead read, CancellationToken cancellationToken) => read.ResourcesAsync(cancellationToken));
        return app;
    }

    private static async Task<IResult> Framework(string key, IWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var detail = await query.FrameworkAsync(key, cancellationToken);
        return detail is null ? Results.NotFound(new { code = "not_found" }) : Results.Ok(detail);
    }

    private static Task<IReadOnlyList<ControlListItem>> Controls(
        IWorkspaceQuery query,
        CancellationToken cancellationToken,
        string? status = null,
        string? q = null,
        string? owner = null) =>
        query.ControlsAsync(status, q, owner, cancellationToken);

    private static async Task<IResult> Control(string code, IWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var detail = await query.ControlAsync(code, cancellationToken);
        return detail is null ? Results.NotFound(new { code = "not_found" }) : Results.Ok(detail);
    }

    private static async Task<IResult> AssignOwner(
        string code,
        AssignOwnerRequest body,
        IWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var result = await query.AssignOwnerAsync(code, body, cancellationToken);
        return EndpointResults.FromResult(result);
    }

    private static async Task<IResult> Activity(IWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var result = await query.ActivityAsync(cancellationToken);
        return EndpointResults.FromResult(result);
    }
}
