using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class ChangeEndpoints
{
    public static IEndpointRouteBuilder MapChangeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/changes", (IChanges portal, CancellationToken cancellationToken) => portal.ListAsync(cancellationToken)).Produces<IReadOnlyList<ChangeItem>>();
        group.MapPost("/changes", Record).RequireAuthorization("TenantWrite").Produces<ChangeItem>();
        group.MapPost("/changes/sync", Sync).RequireAuthorization("TenantWrite").Produces<ChangeSyncResult>();
        group.MapPost("/changes/{id:guid}", Update).RequireAuthorization("TenantWrite").Produces<ChangeItem>();
        return app;
    }

    private static async Task<IResult> Record(RecordChangeRequest body, IChanges portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RecordAsync(body, cancellationToken));

    private static async Task<IResult> Sync(IChanges portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SyncAsync(cancellationToken));

    private static async Task<IResult> Update(Guid id, UpdateChangeRequest body, IChanges portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.UpdateAsync(id, body, cancellationToken));
}
