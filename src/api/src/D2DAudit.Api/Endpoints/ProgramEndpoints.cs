using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class ProgramEndpoints
{
    public static IEndpointRouteBuilder MapProgramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/me/cards", (IProgram portal, CancellationToken cancellationToken) => portal.CardsAsync(cancellationToken)).Produces<IReadOnlyList<MeCard>>();
        group.MapPost("/me/cards", CompleteCard).Produces<MeCard>();
        group.MapGet("/training", (IProgram portal, CancellationToken cancellationToken) => portal.ModulesAsync(cancellationToken)).Produces<IReadOnlyList<TrainingModuleItem>>();
        group.MapGet("/training/{id:guid}", GetModule).Produces<TrainingModuleDetail>();
        group.MapGet("/training/{id:guid}/people", (Guid id, int? page, int? pageSize, IProgram portal, CancellationToken cancellationToken) => portal.TrainingPeopleAsync(id, page ?? 1, pageSize ?? 40, cancellationToken)).Produces<TrainingPersonPage>();
        group.MapPost("/training/{id:guid}/quiz", SubmitQuiz).Produces<QuizResult>();
        group.MapPost("/training/{id:guid}/certificate", UploadCertificate).Produces<QuizResult>();
        group.MapGet("/assets", (int? page, int? pageSize, string? q, IProgram portal, CancellationToken cancellationToken) => portal.AssetsAsync(page ?? 1, pageSize ?? 40, q, cancellationToken)).Produces<AssetPage>();
        group.MapPost("/assets", SaveAsset).RequireAuthorization("TenantWrite").Produces<AssetItem>();
        group.MapPost("/assets/import", ImportAssets).RequireAuthorization("TenantWrite").Produces<int>();
        group.MapGet("/assets/export", ExportAssets).Produces<FileExport>();
        group.MapGet("/tasks", (bool? mine, IProgram portal, CancellationToken cancellationToken) => portal.TasksAsync(mine ?? false, cancellationToken)).Produces<IReadOnlyList<WorkTaskItem>>();
        group.MapPost("/tasks", SaveTask).RequireAuthorization("TenantWrite").Produces<WorkTaskItem>();
        group.MapPost("/tasks/{id:guid}/done", FinishTask).Produces<WorkTaskItem>();
        group.MapGet("/settings/members", (IProgram portal, CancellationToken cancellationToken) => portal.MembersAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<MemberItem>>();
        group.MapPost("/settings/members", Invite).RequireAuthorization("TenantAdmin").Produces<MemberItem>();
        group.MapPost("/settings/members/{id:guid}/role", SetRole).RequireAuthorization("TenantAdmin").Produces<MemberItem>();
        group.MapPost("/settings/members/{id:guid}/deactivate", Deactivate).RequireAuthorization("TenantAdmin").Produces<MemberItem>();
        group.MapGet("/settings/sso", (IProgram portal, CancellationToken cancellationToken) => portal.SsoAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<SsoStatus>>();
        group.MapGet("/settings/grants", (IProgram portal, CancellationToken cancellationToken) => portal.GrantsAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<GrantItem>>();
        group.MapPost("/settings/grants", SaveGrant).RequireAuthorization("TenantAdmin").Produces<GrantItem>();
        return app;
    }

    private static async Task<IResult> CompleteCard(CompleteCardRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.CompleteCardAsync(body, cancellationToken));

    private static async Task<IResult> GetModule(Guid id, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ModuleAsync(id, cancellationToken));

    private static async Task<IResult> SubmitQuiz(Guid id, QuizRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SubmitQuizAsync(id, body, cancellationToken));

    private static async Task<IResult> UploadCertificate(Guid id, CertificateRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.UploadCertificateAsync(id, body, cancellationToken));

    private static async Task<IResult> SaveAsset(SaveAssetRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveAssetAsync(body, cancellationToken));

    private static async Task<IResult> ImportAssets(AssetImportRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ImportAssetsAsync(body, cancellationToken));

    private static async Task<IResult> ExportAssets(IProgram portal, CancellationToken cancellationToken)
    {
        var result = await portal.ExportAssetsAsync(cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }

    private static async Task<IResult> SaveTask(SaveTaskRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveTaskAsync(body, cancellationToken));

    private static async Task<IResult> FinishTask(Guid id, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.FinishTaskAsync(id, cancellationToken));

    private static async Task<IResult> Invite(InviteRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.InviteAsync(body, cancellationToken));

    private static async Task<IResult> SetRole(Guid id, RoleRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SetRoleAsync(id, body, cancellationToken));

    private static async Task<IResult> Deactivate(Guid id, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.DeactivateAsync(id, cancellationToken));

    private static async Task<IResult> SaveGrant(SaveGrantRequest body, IProgram portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveGrantAsync(body, cancellationToken));
}
