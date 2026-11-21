using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization("WorkspaceMember").WithMetadata(new TenantScopedAttribute());
        group.MapGet("/incidents", (IIncidents portal, CancellationToken cancellationToken) => portal.ListAsync(cancellationToken)).RequireAuthorization("SensitiveRead").Produces<IReadOnlyList<IncidentItem>>();
        group.MapPost("/incidents", Report).RequireAuthorization("WorkspaceMember").Produces<IncidentItem>();
        group.MapPost("/incidents/{id:guid}", Update).RequireAuthorization("TenantWrite").Produces<IncidentItem>();
        return app;
    }

    private static async Task<IResult> Report(ReportIncidentRequest body, IIncidents portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ReportAsync(body, cancellationToken));

    private static async Task<IResult> Update(Guid id, UpdateIncidentRequest body, IIncidents portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.UpdateAsync(id, body, cancellationToken));
}
