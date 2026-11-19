using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class EnterpriseEndpoints
{
    public static IEndpointRouteBuilder MapEnterpriseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/public/trust/{slug}/receipt", async (string slug, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        {
            var receipt = await portal.TrustReceiptAsync(slug, cancellationToken);
            return receipt is null ? Results.NotFound(new { code = "not_found" }) : Results.Ok(receipt);
        }).RequireRateLimiting("public_trust");

        app.MapGet("/api/v1/public/trust/{slug}/documents/{documentId:guid}/download", async (
            string slug,
            Guid documentId,
            string? token,
            IEnterprisePortal portal,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var headerToken = http.Request.Headers["X-Trust-Token"].FirstOrDefault();
            var effectiveToken = !string.IsNullOrWhiteSpace(headerToken) ? headerToken : token;
            var result = await portal.DownloadTrustDocumentAsync(slug, documentId, effectiveToken, cancellationToken);
            if (!result.IsSuccess || result.Value is null)
            {
                return result.Error == "nda_required"
                    ? Results.Json(new { code = "nda_required" }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.NotFound(new { code = result.Error ?? "not_found" });
            }

            if (!string.IsNullOrWhiteSpace(result.Value.Sha256))
            {
                http.Response.Headers["X-Document-Sha256"] = result.Value.Sha256;
            }

            return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
        }).RequireRateLimiting("public_trust");

        app.MapPost("/api/v1/public/trust/{slug}/ask", async (string slug, AssistantAskRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.AskTrustPublicAsync(slug, body, cancellationToken)))
            .RequireRateLimiting("public_trust");

        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/frameworks/{key}/simulator", SimulateFramework)
            .RequireAuthorization("WorkspaceMember");
        group.MapPost("/frameworks/{key}/simulator/adopt", AdoptFrameworkGaps).RequireAuthorization("TenantWrite");
        group.MapPost("/frameworks/custom", CreateCustomFramework).RequireAuthorization("TenantWrite");
        group.MapPost("/assistant/ask", AskWorkspace);

        group.MapGet("/developer/keys", (IEnterprisePortal portal, CancellationToken cancellationToken) => portal.ListApiKeysAsync(cancellationToken))
            .RequireAuthorization("TenantAdmin")
            .Produces<IReadOnlyList<ApiKeyDto>>();
        group.MapPost("/developer/keys", CreateApiKey)
            .RequireAuthorization("TenantAdmin")
            .Produces<CreatedApiKeyDto>();
        group.MapDelete("/developer/keys/{id:guid}", RevokeApiKey)
            .RequireAuthorization("TenantAdmin");

        group.MapGet("/developer/webhooks", (IEnterprisePortal portal, CancellationToken cancellationToken) => portal.ListWebhooksAsync(cancellationToken))
            .RequireAuthorization("TenantAdmin")
            .Produces<IReadOnlyList<WebhookDto>>();
        group.MapPost("/developer/webhooks", CreateWebhook)
            .RequireAuthorization("TenantAdmin")
            .Produces<CreatedWebhookDto>();
        group.MapPost("/developer/webhooks/{id:guid}/test", TestWebhook)
            .RequireAuthorization("TenantAdmin")
            .Produces<WebhookTestResultDto>();
        group.MapDelete("/developer/webhooks/{id:guid}", DeleteWebhook)
            .RequireAuthorization("TenantAdmin");

        return app;
    }

    private static async Task<IResult> SimulateFramework(string key, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SimulateFrameworkAsync(key, cancellationToken));

    private static async Task<IResult> AdoptFrameworkGaps(string key, AdoptGapsRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AdoptFrameworkGapsAsync(key, body, cancellationToken));

    private static async Task<IResult> CreateCustomFramework(CreateCustomFrameworkRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateCustomFrameworkAsync(body, cancellationToken));

    private static async Task<IResult> AskWorkspace(AssistantAskRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AskWorkspaceAsync(body, cancellationToken));

    private static async Task<IResult> CreateApiKey(CreateApiKeyRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateApiKeyAsync(body, cancellationToken));

    private static async Task<IResult> RevokeApiKey(Guid id, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RevokeApiKeyAsync(id, cancellationToken));

    private static async Task<IResult> CreateWebhook(CreateWebhookRequest body, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CreateWebhookAsync(body, cancellationToken));

    private static async Task<IResult> TestWebhook(Guid id, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.TestWebhookAsync(id, cancellationToken));

    private static async Task<IResult> DeleteWebhook(Guid id, IEnterprisePortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.DeleteWebhookAsync(id, cancellationToken));
}
