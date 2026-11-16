using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class WorkforceEndpoints
{
    public static IEndpointRouteBuilder MapWorkforceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapPost("/people/import/okta", Okta).RequireAuthorization("TenantWrite").Produces<ImportCount>();
        group.MapPost("/people/import/hris", Hris).RequireAuthorization("TenantWrite").Produces<ImportCount>();
        return app;
    }

    private static async Task<IResult> Okta(IWorkforceImport portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.OktaAsync(cancellationToken));

    private static async Task<IResult> Hris(IWorkforceImport portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.HrisAsync(cancellationToken));
}
