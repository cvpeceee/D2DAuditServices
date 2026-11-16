using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class SystemDescriptionEndpoints
{
    public static IEndpointRouteBuilder MapSystemDescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/system-description", (ISystemDescription portal, CancellationToken cancellationToken) => portal.GetAsync(cancellationToken)).Produces<SystemDescriptionDto>();
        group.MapPost("/system-description", Save).RequireAuthorization("TenantWrite").Produces<SystemDescriptionDto>();
        group.MapPost("/system-description/redraft", Redraft).RequireAuthorization("TenantWrite").Produces<SystemDescriptionDto>();
        return app;
    }

    private static async Task<IResult> Save(SaveSystemDescriptionRequest body, ISystemDescription portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveAsync(body, cancellationToken));

    private static async Task<IResult> Redraft(ISystemDescription portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RedraftAsync(cancellationToken));
}
