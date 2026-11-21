using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class AssuranceEndpoints
{
    private const string Write = "TenantWrite";

    public static IEndpointRouteBuilder MapAssuranceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());

        group.MapGet("/tests/custom", (ICustomTestPortal portal, CancellationToken ct) => portal.ListAsync(ct))
            .Produces<IReadOnlyList<CustomTestDto>>();
        group.MapGet("/tests/resource-types", (ICustomTestPortal portal, CancellationToken ct) => portal.ResourceTypesAsync(ct))
            .Produces<IReadOnlyList<ResourceTypeDto>>();
        group.MapGet("/tests/states", (ICustomTestPortal portal, CancellationToken ct) => portal.StatesAsync(ct))
            .Produces<IReadOnlyList<TestStateDto>>();
        group.MapPost("/tests/custom/preview", async (PreviewCustomTestRequest body, ICustomTestPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.PreviewAsync(body, ct))).RequireAuthorization(Write).Produces<CustomTestPreview>();
        group.MapPost("/tests/custom", async (SaveCustomTestRequest body, ICustomTestPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.CreateAsync(body, ct))).RequireAuthorization(Write).Produces<CustomTestDto>();
        group.MapPut("/tests/custom/{id:guid}", async (Guid id, SaveCustomTestRequest body, ICustomTestPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.UpdateAsync(id, body, ct))).RequireAuthorization(Write).Produces<CustomTestDto>();
        group.MapDelete("/tests/custom/{id:guid}", async (Guid id, ICustomTestPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.DeleteAsync(id, ct))).RequireAuthorization(Write).Produces<bool>();
        group.MapPut("/tests/{key}/state", async (string key, SetTestStateRequest body, ICustomTestPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.SetStateAsync(key, body, ct))).RequireAuthorization(Write).Produces<TestStateDto>();

        group.MapGet("/evidence/library", (IEvidenceLibraryPortal portal, CancellationToken ct) => portal.ListAsync(ct))
            .Produces<EvidenceLibraryPage>();
        group.MapPost("/evidence/library", async (CreateEvidenceDocumentRequest body, IEvidenceLibraryPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.CreateAsync(body, ct))).RequireAuthorization(Write).Produces<EvidenceDocumentDto>();
        group.MapPost("/evidence/library/links", async (BulkLinkEvidenceRequest body, IEvidenceLibraryPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.LinkAsync(body, ct))).RequireAuthorization(Write).Produces<int>();
        group.MapPost("/evidence/library/renewals", async (IEvidenceLibraryPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.CreateRenewalTasksAsync(ct))).RequireAuthorization(Write).Produces<int>();
        group.MapPost("/evidence/library/{code}/requests", async (string code, CreateEvidenceRequestRequest body, IEvidenceLibraryPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.RequestAsync(code, body, ct))).RequireAuthorization(Write).Produces<EvidenceDocumentDto>();
        group.MapPost("/evidence/library/{code}/upload", async (string code, UploadLibraryEvidenceRequest body, IEvidenceLibraryPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.UploadAsync(code, body, ct))).Produces<EvidenceDocumentDto>();

        group.MapGet("/controls/{code}/readiness", async (string code, IControlInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.ReadinessAsync(code, ct))).Produces<ControlReadinessDto>();
        group.MapPut("/controls/{code}/profile", async (string code, UpdateControlProfileRequest body, IControlInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.UpdateProfileAsync(code, body, ct))).RequireAuthorization(Write).Produces<ControlReadinessDto>();
        group.MapGet("/controls/{code}/comments", async (string code, IControlInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.CommentsAsync(code, ct))).Produces<IReadOnlyList<ControlCommentDto>>();
        group.MapPost("/controls/{code}/comments", async (string code, ControlCommentRequest body, IControlInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.AddCommentAsync(code, body, ct))).RequireAuthorization(Write).Produces<ControlCommentDto>();
        group.MapPut("/controls/{code}/comments/{id:guid}", async (string code, Guid id, ControlCommentRequest body, IControlInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.EditCommentAsync(code, id, body, ct))).RequireAuthorization(Write).Produces<ControlCommentDto>();

        group.MapGet("/policies/{code}/diff", async (string code, int from, int to, IPolicyInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.DiffAsync(code, from, to, ct))).Produces<PolicyDiffDto>();
        group.MapGet("/policies/{code}/attestations", async (string code, IPolicyInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.AttestationsAsync(code, ct))).Produces<IReadOnlyList<AttestationCampaignDto>>();
        group.MapPost("/policies/{code}/attestations", async (string code, StartAttestationRequest body, IPolicyInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.StartAttestationAsync(code, body, ct))).RequireAuthorization(Write).Produces<AttestationCampaignDto>();
        group.MapPost("/policies/{code}/attestations/{id:guid}/renew", async (string code, Guid id, IPolicyInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.RenewAttestationAsync(code, id, ct))).RequireAuthorization(Write).Produces<AttestationCampaignDto>();
        group.MapPost("/policies/{code}/attest", async (string code, IPolicyInsightPortal portal, CancellationToken ct) =>
            EndpointResults.FromResult(await portal.AttestAsync(code, ct))).Produces<AttestationCampaignDto>();
        return app;
    }
}
