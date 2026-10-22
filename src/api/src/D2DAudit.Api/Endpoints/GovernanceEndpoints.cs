using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/risks", (IGovernance portal, CancellationToken cancellationToken) => portal.RisksAsync(cancellationToken)).Produces<IReadOnlyList<RiskListItem>>();
        group.MapPost("/risks", SaveRisk).RequireAuthorization("TenantWrite").Produces<RiskListItem>();
        group.MapGet("/risks/export", ExportRisks).Produces<FileExport>();
        group.MapGet("/people", (int? page, int? pageSize, string? q, IGovernance portal, CancellationToken cancellationToken) =>
            portal.PeopleAsync(page ?? 1, pageSize ?? 40, q, cancellationToken)).Produces<PersonPage>();
        group.MapPost("/people/sync", SyncPeople).RequireAuthorization("TenantWrite").Produces<int>();
        group.MapGet("/people/{id:guid}", GetPerson).Produces<PersonDetail>();
        group.MapPost("/people/{id:guid}/tasks", SetTask).RequireAuthorization("TenantWrite").Produces<PersonDetail>();
        group.MapGet("/soa", (IGovernance portal, CancellationToken cancellationToken) => portal.SoaAsync(cancellationToken)).Produces<IReadOnlyList<SoaRow>>();
        group.MapPut("/soa/{code}", SaveSoa).RequireAuthorization("TenantWrite").Produces<SoaRow>();
        group.MapGet("/soa/export", ExportSoa).Produces<FileExport>();
        group.MapGet("/vendors", (IGovernance portal, CancellationToken cancellationToken) => portal.VendorsAsync(cancellationToken)).Produces<IReadOnlyList<VendorListItem>>();
        group.MapPost("/vendors", SaveVendor).RequireAuthorization("TenantWrite").Produces<VendorListItem>();
        group.MapGet("/access-reviews", (IGovernance portal, CancellationToken cancellationToken) => portal.CampaignsAsync(cancellationToken)).Produces<IReadOnlyList<AccessCampaignListItem>>();
        group.MapPost("/access-reviews", StartCampaign).RequireAuthorization("TenantWrite").Produces<AccessCampaignDetail>();
        group.MapGet("/access-reviews/{id:guid}", GetCampaign).Produces<AccessCampaignDetail>();
        group.MapPost("/access-reviews/{id:guid}/items/{itemId:guid}", Decide).RequireAuthorization("TenantWrite").Produces<AccessCampaignDetail>();
        group.MapPost("/access-reviews/{id:guid}/complete", Complete).RequireAuthorization("TenantWrite").Produces<AccessCampaignDetail>();
        return app;
    }

    private static async Task<IResult> SaveRisk(SaveRiskRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveRiskAsync(body, cancellationToken));

    private static async Task<IResult> ExportRisks(string format, IGovernance portal, CancellationToken cancellationToken) =>
        await FileAsync(portal.ExportRisksAsync(format, cancellationToken));

    private static async Task<IResult> SyncPeople(IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SyncPeopleAsync(cancellationToken));

    private static async Task<IResult> GetPerson(Guid id, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.PersonAsync(id, cancellationToken));

    private static async Task<IResult> SetTask(Guid id, SetTaskRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SetTaskAsync(id, body, cancellationToken));

    private static async Task<IResult> SaveSoa(string code, SaveSoaRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveSoaAsync(code, body, cancellationToken));

    private static async Task<IResult> ExportSoa(string format, IGovernance portal, CancellationToken cancellationToken) =>
        await FileAsync(portal.ExportSoaAsync(format, cancellationToken));

    private static async Task<IResult> SaveVendor(SaveVendorRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveVendorAsync(body, cancellationToken));

    private static async Task<IResult> StartCampaign(StartCampaignRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.StartCampaignAsync(body, cancellationToken));

    private static async Task<IResult> GetCampaign(Guid id, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CampaignAsync(id, cancellationToken));

    private static async Task<IResult> Decide(Guid id, Guid itemId, AccessDecisionRequest body, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.DecideAsync(id, itemId, body, cancellationToken));

    private static async Task<IResult> Complete(Guid id, IGovernance portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CompleteCampaignAsync(id, cancellationToken));

    private static async Task<IResult> FileAsync(Task<Result<FileExport>> work)
    {
        var result = await work;
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }
}
