using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class AcademyEndpoints
{
    public static IEndpointRouteBuilder MapAcademyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/academy").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("", Overview).Produces<AcademyOverview>();
        group.MapGet("/assignments", Assignments).Produces<IReadOnlyList<AcademyAssignmentItem>>();
        group.MapPut("/assignments/{key}", SaveAssignment).RequireAuthorization("TenantWrite").Produces<AcademyAssignmentItem>();
        group.MapGet("/{key}", Module).Produces<AcademyModuleDetail>();
        group.MapPost("/{key}/progress", Progress).Produces<AcademyModuleItem>();
        group.MapPost("/{key}/quiz", Quiz).Produces<AcademyQuizResult>();
        group.MapGet("/{key}/certificate", Certificate).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        return app;
    }

    private static async Task<IResult> Overview(IAcademy academy, CancellationToken cancellationToken) =>
        Results.Ok(await academy.OverviewAsync(cancellationToken));

    private static async Task<IResult> Module(string key, IAcademy academy, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await academy.ModuleAsync(key, cancellationToken));

    private static async Task<IResult> Progress(string key, AcademyProgressRequest body, IAcademy academy, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await academy.ProgressAsync(key, body, cancellationToken));

    private static async Task<IResult> Quiz(string key, AcademyQuizRequest body, IAcademy academy, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await academy.QuizAsync(key, body, cancellationToken));

    private static async Task<IResult> Certificate(string key, IAcademy academy, CancellationToken cancellationToken)
    {
        var result = await academy.CertificateAsync(key, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }

    private static async Task<IResult> Assignments(IAcademy academy, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await academy.AssignmentsAsync(cancellationToken));

    private static async Task<IResult> SaveAssignment(string key, SaveAcademyAssignmentRequest body, IAcademy academy, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await academy.SaveAssignmentAsync(key, body, cancellationToken));
}
