using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/audits", (IAuditPortal portal, CancellationToken cancellationToken) => portal.GetAsync(cancellationToken)).Produces<AuditPortalPage>();
        group.MapPost("/audits/requests", CreateRequest).RequireAuthorization("AuditAct").Produces<SampleRequestDto>();
        group.MapPost("/audits/requests/{code}/respond", Respond).RequireAuthorization("TenantWrite").Produces<SampleRequestDto>();
        group.MapPost("/audits/requests/{code}/accept", Accept).RequireAuthorization("AuditAct").Produces<SampleRequestDto>();
        group.MapPost("/audits/findings", CreateFinding).RequireAuthorization("AuditAct").Produces<FindingDto>();
        group.MapPost("/audits/findings/{code}/response", RespondFinding).RequireAuthorization("TenantWrite").Produces<FindingDto>();
        group.MapPost("/audits/package", Export).RequireAuthorization("AuditAct").Produces<Guid>();
        group.MapGet("/audits/packages/{id:guid}", Download).RequireAuthorization("AuditAct");
        return app;
    }

    private static async Task<IResult> CreateRequest(CreateSampleRequest body, IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateRequestAsync(body, cancellationToken));

    private static async Task<IResult> Respond(string code, RespondSampleRequest body, IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RespondAsync(code, body, cancellationToken));

    private static async Task<IResult> Accept(string code, IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AcceptAsync(code, cancellationToken));

    private static async Task<IResult> CreateFinding(CreateFindingRequest body, IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateFindingAsync(body, cancellationToken));

    private static async Task<IResult> RespondFinding(string code, ManagementResponseRequest body, IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RespondToFindingAsync(code, body, cancellationToken));

    private static async Task<IResult> Export(IAuditPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.StartExportAsync(cancellationToken));

    private static async Task<IResult> Download(Guid id, IAuditPortal portal, CancellationToken cancellationToken)
    {
        var result = await portal.OpenPackageAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Bytes, "application/zip", result.Value.FileName);
    }
}
