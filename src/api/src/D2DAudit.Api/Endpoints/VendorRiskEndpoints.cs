using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class VendorRiskEndpoints
{
    public static IEndpointRouteBuilder MapVendorRiskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/vendor-risk").RequireAuthorization("WorkspaceMember").WithMetadata(new TenantScopedAttribute());
        group.MapGet("/inventory", (IVendorRisk portal, CancellationToken cancellationToken) => portal.InventoryAsync(cancellationToken)).Produces<IReadOnlyList<VendorRiskRow>>();
        group.MapGet("/vendors/{id:guid}", async (Guid id, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.DetailAsync(id, cancellationToken))).Produces<VendorRiskDetailDto>();
        group.MapPost("/vendors/{id:guid}/profile", async (Guid id, SaveVendorProfileRequest body, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SaveProfileAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorRiskRow>();
        group.MapGet("/discovery", (IVendorRisk portal, CancellationToken cancellationToken) => portal.DiscoveryAsync(cancellationToken)).Produces<IReadOnlyList<VendorDiscoveryDto>>();
        group.MapPost("/discovery/scan", async (IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.ScanAsync(cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorDiscoveryScanResult>();
        group.MapPost("/discovery/{id:guid}/accept", async (Guid id, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.DecideAsync(id, true, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorDiscoveryDto>();
        group.MapPost("/discovery/{id:guid}/ignore", async (Guid id, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.DecideAsync(id, false, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorDiscoveryDto>();
        group.MapGet("/templates", (IVendorRisk portal) => portal.Templates()).Produces<IReadOnlyList<QuestionnaireTemplateDto>>();
        group.MapGet("/assessments", (IVendorRisk portal, CancellationToken cancellationToken) => portal.AssessmentsAsync(cancellationToken)).Produces<IReadOnlyList<VendorAssessmentDto>>();
        group.MapPost("/vendors/{id:guid}/questionnaires", async (Guid id, SendVendorQuestionnaireRequest body, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SendAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<SentVendorQuestionnaire>();
        group.MapPost("/assessments/{id:guid}/review", async (Guid id, ReviewVendorAssessmentRequest body, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.ReviewAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorAssessmentDto>();
        group.MapPost("/assessments/{id:guid}/revoke", async (Guid id, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.RevokeAsync(id, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorAssessmentDto>();
        group.MapGet("/documents", (IVendorRisk portal, CancellationToken cancellationToken) => portal.DocumentsAsync(cancellationToken)).Produces<IReadOnlyList<VendorReviewDocumentDto>>();
        group.MapPost("/vendors/{id:guid}/documents", async (Guid id, UploadVendorDocumentRequest body, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.UploadAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorReviewDocumentDto>();
        group.MapPost("/documents/{id:guid}/summarize", async (Guid id, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SummarizeAsync(id, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorReviewDocumentDto>();
        group.MapPost("/documents/{id:guid}/sign-off", async (Guid id, SignOffVendorDocumentRequest body, IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SignOffAsync(id, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorReviewDocumentDto>();
        group.MapPost("/reminders/sync", async (IVendorRisk portal, CancellationToken cancellationToken) =>
            EndpointResults.FromResult(await portal.SyncRemindersAsync(cancellationToken))).RequireAuthorization("TenantWrite").Produces<VendorReminderSyncResult>();

        app.MapGet("/api/v1/public/vendor-portal/{token}", async (string token, IVendorPortal portal, CancellationToken cancellationToken) =>
            Portal(await portal.GetAsync(token, cancellationToken))).RequireRateLimiting("public_trust").Produces<VendorPortalView>();
        app.MapPost("/api/v1/public/vendor-portal/{token}/answers", async (string token, VendorPortalAnswersRequest body, IVendorPortal portal, CancellationToken cancellationToken) =>
            Portal(await portal.SaveAnswersAsync(token, body, cancellationToken))).RequireRateLimiting("public_trust").Produces<VendorPortalView>();
        app.MapPost("/api/v1/public/vendor-portal/{token}/documents", async (string token, UploadVendorDocumentRequest body, IVendorPortal portal, CancellationToken cancellationToken) =>
            Portal(await portal.UploadAsync(token, body, cancellationToken))).RequireRateLimiting("public_trust").Produces<VendorPortalView>();
        return app;
    }

    private static IResult Portal(Result<VendorPortalView> result) =>
        result.Error == "expired"
            ? Results.Json(new { code = "expired" }, statusCode: StatusCodes.Status410Gone)
            : EndpointResults.FromResult(result);
}
