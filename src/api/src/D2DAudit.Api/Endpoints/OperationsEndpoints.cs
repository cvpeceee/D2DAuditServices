using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/public/trust/{slug}", async (string slug, IOperations portal, CancellationToken cancellationToken) =>
        {
            var page = await portal.TrustPublicAsync(slug, cancellationToken);
            return page is null ? Results.NotFound(new { code = "not_found" }) : Results.Ok(page);
        });
        app.MapPost("/api/v1/public/trust/{slug}/requests", async (string slug, TrustAccessRequestBody body, IOperations portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RequestTrustAccessAsync(slug, body, cancellationToken)))
            .RequireRateLimiting("public_trust");

        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/computers", (IOperations portal, CancellationToken cancellationToken) => portal.ComputersAsync(cancellationToken)).RequireAuthorization("WorkspaceMember").Produces<IReadOnlyList<ComputerItem>>();
        group.MapPost("/computers", SaveComputer).RequireAuthorization("TenantWrite").Produces<ComputerItem>();
        group.MapPost("/computers/{id:guid}/screenshot", UploadScreenshot).RequireAuthorization("TenantWrite").Produces<ComputerItem>();
        group.MapGet("/vulnerabilities", (IOperations portal, CancellationToken cancellationToken) => portal.VulnerabilitiesAsync(cancellationToken)).RequireAuthorization("WorkspaceMember").Produces<VulnerabilityBoard>();
        group.MapPost("/vulnerabilities/{id:guid}/ignore", Ignore).RequireAuthorization("TenantWrite").Produces<VulnerabilityItem>();
        group.MapPost("/vulnerabilities/sla", SaveSla).RequireAuthorization("TenantWrite").Produces<VulnerabilitySlaDto>();
        group.MapGet("/trust", (IOperations portal, CancellationToken cancellationToken) => portal.TrustAdminAsync(cancellationToken)).RequireAuthorization("TenantWrite").Produces<TrustAdminPage>();
        group.MapPost("/trust", SaveTrust).RequireAuthorization("TenantWrite").Produces<TrustAdminPage>();
        group.MapPost("/trust/documents", SaveDocument).RequireAuthorization("TenantWrite").Produces<TrustDocumentDto>();
        group.MapPost("/trust/requests/{id:guid}", Decide).RequireAuthorization("TenantWrite").Produces<TrustRequestDto>();
        group.MapGet("/settings/automation", (IOperations portal, CancellationToken cancellationToken) => portal.RulesAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<AutomationRuleDto>>();
        group.MapGet("/settings/automation/deliveries", (IOperations portal, CancellationToken cancellationToken) => portal.DeliveriesAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<AutomationDeliveryDto>>();
        group.MapPost("/settings/automation", SaveRule).RequireAuthorization("TenantAdmin").Produces<AutomationRuleDto>();
        group.MapPost("/settings/automation/run", RunDue).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<AutomationDeliveryDto>>();
        group.MapPost("/controls", CreateControl).RequireAuthorization("TenantWrite").Produces<CreatedControl>();
        group.MapPost("/controls/import", ImportControls).RequireAuthorization("TenantWrite").Produces<int>();
        return app;
    }

    private static async Task<IResult> SaveComputer(SaveComputerRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveComputerAsync(body, cancellationToken));

    private static async Task<IResult> UploadScreenshot(Guid id, ScreenshotRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.UploadScreenshotAsync(id, body, cancellationToken));

    private static async Task<IResult> Ignore(Guid id, IgnoreVulnerabilityRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.IgnoreVulnerabilityAsync(id, body, cancellationToken));

    private static async Task<IResult> SaveSla(VulnerabilitySlaDto body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveSlaAsync(body, cancellationToken));

    private static async Task<IResult> SaveTrust(SaveTrustRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveTrustAsync(body, cancellationToken));

    private static async Task<IResult> SaveDocument(SaveTrustDocumentRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveTrustDocumentAsync(body, cancellationToken));

    private static async Task<IResult> Decide(Guid id, DecideTrustRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.DecideTrustAsync(id, body, cancellationToken));

    private static async Task<IResult> SaveRule(SaveAutomationRuleRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveRuleAsync(body, cancellationToken));

    private static async Task<IResult> RunDue(IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RunDueAsync(cancellationToken));

    private static async Task<IResult> CreateControl(CreateControlRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateControlAsync(body, cancellationToken));

    private static async Task<IResult> ImportControls(ImportControlsRequest body, IOperations portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ImportControlsAsync(body, cancellationToken));
}
