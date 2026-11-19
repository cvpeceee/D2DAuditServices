using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class CloudSecurityEndpoints
{
    public static IEndpointRouteBuilder MapCloudSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/security").RequireAuthorization("WorkspaceMember").WithMetadata(new TenantScopedAttribute());
        group.MapGet("/exposure", (ICloudSecurity security, CancellationToken cancellationToken) => security.ExposureBoardAsync(cancellationToken))
            .Produces<ExposureBoardDto>();
        group.MapPost("/exposure/scan", ScanExposure)
            .RequireAuthorization("TenantWrite")
            .Produces<ExposureBoardDto>();
        group.MapPost("/exposure/{id:guid}/resolve", ResolveExposure)
            .RequireAuthorization("TenantWrite")
            .Produces<ExposureItemDto>();
        group.MapGet("/scans", (ICloudSecurity security, CancellationToken cancellationToken) => security.ScansBoardAsync(cancellationToken))
            .Produces<SecurityScansBoardDto>();
        group.MapPost("/scans/sarif", IngestSarif)
            .RequireAuthorization("TenantWrite")
            .Produces<int>();
        group.MapPost("/scans/{id:guid}/resolve", ResolveScan)
            .RequireAuthorization("TenantWrite")
            .Produces<ScanFindingDto>();
        group.MapGet("/dspm", (ICloudSecurity security, CancellationToken cancellationToken) => security.DspmBoardAsync(cancellationToken))
            .Produces<DspmBoardDto>();
        group.MapPost("/dspm", SaveDataStore)
            .RequireAuthorization("TenantWrite")
            .Produces<DataStoreDto>();
        group.MapPost("/dspm/scan", ScanDspm)
            .RequireAuthorization("TenantWrite")
            .Produces<DspmBoardDto>();
        group.MapGet("/finops", (ICloudSecurity security, CancellationToken cancellationToken) => security.FinOpsBoardAsync(cancellationToken))
            .Produces<FinOpsBoardDto>();
        group.MapPost("/finops/{id:guid}/status", UpdateCostStatus)
            .RequireAuthorization("TenantWrite")
            .Produces<CostRecommendationDto>();
        group.MapGet("/attack-paths", (ICloudSecurity security, CancellationToken cancellationToken) => security.AttackPathsAsync(cancellationToken))
            .Produces<AttackPathsBoardDto>();
        return app;
    }

    private static async Task<IResult> ScanExposure(ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.ScanExposureAsync(cancellationToken));

    private static async Task<IResult> ResolveExposure(Guid id, ResolveFindingRequest body, ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.ResolveExposureAsync(id, body, cancellationToken));

    private static async Task<IResult> IngestSarif(IngestSarifRequest body, ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.IngestSarifAsync(body, cancellationToken));

    private static async Task<IResult> ResolveScan(Guid id, ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.ResolveScanAsync(id, cancellationToken));

    private static async Task<IResult> SaveDataStore(SaveDataStoreRequest body, ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.SaveDataStoreAsync(body, cancellationToken));

    private static async Task<IResult> ScanDspm(ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.ScanDspmAsync(cancellationToken));

    private static async Task<IResult> UpdateCostStatus(Guid id, UpdateCostStatusRequest body, ICloudSecurity security, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await security.UpdateCostStatusAsync(id, body, cancellationToken));
}
