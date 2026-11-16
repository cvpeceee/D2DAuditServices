using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class DeviceFleetEndpoints
{
    public static IEndpointRouteBuilder MapDeviceFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapPost("/computers/check-in", CheckIn).RequireAuthorization("TenantWrite").Produces<ComputerItem>();
        group.MapPost("/computers/mdm-sync", Sync).RequireAuthorization("TenantWrite").Produces<MdmSyncResult>();
        return app;
    }

    private static async Task<IResult> CheckIn(AgentCheckInRequest body, IDeviceFleet fleet, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await fleet.CheckInAsync(body, cancellationToken));

    private static async Task<IResult> Sync(IDeviceFleet fleet, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await fleet.SyncAsync(cancellationToken));
}
