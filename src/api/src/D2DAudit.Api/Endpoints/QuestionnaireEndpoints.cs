using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class QuestionnaireEndpoints
{
    public static IEndpointRouteBuilder MapQuestionnaireEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/questionnaires", (IQuestionnaires portal, CancellationToken cancellationToken) => portal.ListAsync(cancellationToken)).Produces<IReadOnlyList<QuestionnaireSummary>>();
        group.MapGet("/questionnaires/{id:guid}", Get).Produces<QuestionnaireDetail>();
        group.MapGet("/questionnaires/{id:guid}/export", Export);
        group.MapPost("/questionnaires", Upload).RequireAuthorization("TenantWrite").Produces<QuestionnaireDetail>();
        group.MapPost("/questionnaires/{id:guid}/answers/{answerId:guid}", Review).RequireAuthorization("TenantWrite").Produces<QuestionnaireAnswerItem>();
        return app;
    }

    private static async Task<IResult> Get(Guid id, IQuestionnaires portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.GetAsync(id, cancellationToken));

    private static async Task<IResult> Upload(UploadQuestionnaireRequest body, IQuestionnaires portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.UploadAsync(body, cancellationToken));

    private static async Task<IResult> Review(Guid id, Guid answerId, ReviewAnswerRequest body, IQuestionnaires portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ReviewAsync(id, answerId, body, cancellationToken));

    private static async Task<IResult> Export(Guid id, IQuestionnaires portal, CancellationToken cancellationToken)
    {
        var result = await portal.ExportAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }
}
