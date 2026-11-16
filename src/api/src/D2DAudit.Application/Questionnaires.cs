namespace D2DAudit.Application;

public sealed record QuestionnaireSummary(
    Guid Id,
    string Name,
    string CustomerName,
    string Status,
    DateTimeOffset CreatedAt,
    int Questions,
    int Accepted);

public sealed record QuestionnaireAnswerItem(
    Guid Id,
    int Position,
    string Question,
    string Suggestion,
    string Citation,
    string Answer,
    string ReviewerName,
    bool Accepted);

public sealed record QuestionnaireDetail(
    Guid Id,
    string Name,
    string CustomerName,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<QuestionnaireAnswerItem> Answers);

public sealed record UploadQuestionnaireRequest(string Name, string CustomerName, string FileName, string ContentBase64);

public sealed record ReviewAnswerRequest(string? Answer, bool? Accepted);

public interface IQuestionnaireAssistant
{
    (string Answer, string Citation) Suggest(string question, IReadOnlyList<(string Citation, string Text)> knowledge);
}

public interface IQuestionnaires
{
    Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Result<QuestionnaireDetail>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<QuestionnaireDetail>> UploadAsync(UploadQuestionnaireRequest request, CancellationToken cancellationToken);

    Task<Result<QuestionnaireAnswerItem>> ReviewAsync(Guid id, Guid answerId, ReviewAnswerRequest request, CancellationToken cancellationToken);

    Task<Result<FileExport>> ExportAsync(Guid id, CancellationToken cancellationToken);
}
