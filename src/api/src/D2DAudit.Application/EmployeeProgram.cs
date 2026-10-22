namespace D2DAudit.Application;

public sealed record MeCard(
    string Kind,
    string Title,
    string Status,
    DateOnly? DueOn,
    string Action,
    Guid? EvidenceId,
    string? EvidenceSha256);

public sealed record CompleteCardRequest(string Kind, string? Note, string? Body);

public sealed record TrainingModuleItem(
    Guid Id,
    string Code,
    string Title,
    string Type,
    string AssignedGroup,
    DateOnly DueOn,
    int Assigned,
    int Completed,
    string? ExternalUrl);

public sealed record TrainingQuestionDto(string Prompt, IReadOnlyList<string> Choices);

public sealed record TrainingModuleDetail(
    Guid Id,
    string Code,
    string Title,
    string Type,
    string AssignedGroup,
    DateOnly DueOn,
    string? ExternalUrl,
    IReadOnlyList<TrainingSlideDto> Slides,
    IReadOnlyList<TrainingQuestionDto> Questions,
    bool Completed,
    int? Score,
    Guid? EvidenceId,
    string? EvidenceSha256);

public sealed record TrainingSlideDto(string Title, string Body);

public sealed record TrainingPersonPage(IReadOnlyList<TrainingPersonItem> Items, int Total, int Page, int PageSize);

public sealed record TrainingPersonItem(string Name, string Email, string Department, string Status, DateOnly? CompletedOn, DateOnly? DueOn);

public sealed record QuizRequest(IReadOnlyList<int> Answers);

public sealed record QuizResult(bool Passed, int Score, int Total, Guid? EvidenceId, string? EvidenceSha256);

public sealed record CertificateRequest(string FileName, string Body);

public sealed record AssetPage(IReadOnlyList<AssetItem> Items, int Total, int Page, int PageSize);

public sealed record AssetItem(
    Guid Id,
    string Name,
    string Type,
    string Source,
    string OwnerName,
    string OwnerEmail,
    string Environment,
    string Classification,
    bool InScope,
    DateTimeOffset LastSeen);

public sealed record SaveAssetRequest(
    Guid? Id,
    string Name,
    string Type,
    string Source,
    string OwnerName,
    string OwnerEmail,
    string Environment,
    string Classification,
    bool InScope);

public sealed record AssetImportRequest(string Csv);

public sealed record WorkTaskItem(
    Guid Id,
    string Title,
    string LinkedKind,
    string LinkedCode,
    string OwnerName,
    string OwnerEmail,
    DateOnly DueOn,
    string Status,
    string Recurrence,
    bool Overdue);

public sealed record SaveTaskRequest(
    Guid? Id,
    string Title,
    string LinkedKind,
    string LinkedCode,
    string OwnerName,
    string OwnerEmail,
    DateOnly DueOn,
    string Status,
    string Recurrence);

public sealed record MemberItem(Guid Id, string Name, string Email, string Role, bool Active);

public sealed record InviteRequest(string Name, string Email, string Role);

public sealed record RoleRequest(string Role);

public sealed record SsoStatus(string Domain, bool Connected, string? OrganizationId);

public sealed record GrantItem(
    Guid Id,
    string Email,
    string Name,
    string AuditName,
    string FirmName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly AccessExpiresOn,
    IReadOnlyList<string> Frameworks);

public sealed record SaveGrantRequest(
    string Email,
    string AuditName,
    string FirmName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly AccessExpiresOn,
    IReadOnlyList<string> Frameworks);

public interface IProgram
{
    Task<IReadOnlyList<MeCard>> CardsAsync(CancellationToken cancellationToken);

    Task<Result<MeCard>> CompleteCardAsync(CompleteCardRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrainingModuleItem>> ModulesAsync(CancellationToken cancellationToken);

    Task<Result<TrainingModuleDetail>> ModuleAsync(Guid id, CancellationToken cancellationToken);

    Task<TrainingPersonPage> TrainingPeopleAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);

    Task<Result<QuizResult>> SubmitQuizAsync(Guid id, QuizRequest request, CancellationToken cancellationToken);

    Task<Result<QuizResult>> UploadCertificateAsync(Guid id, CertificateRequest request, CancellationToken cancellationToken);

    Task<AssetPage> AssetsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);

    Task<Result<AssetItem>> SaveAssetAsync(SaveAssetRequest request, CancellationToken cancellationToken);

    Task<Result<int>> ImportAssetsAsync(AssetImportRequest request, CancellationToken cancellationToken);

    Task<Result<FileExport>> ExportAssetsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkTaskItem>> TasksAsync(bool mine, CancellationToken cancellationToken);

    Task<Result<WorkTaskItem>> SaveTaskAsync(SaveTaskRequest request, CancellationToken cancellationToken);

    Task<Result<WorkTaskItem>> FinishTaskAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberItem>> MembersAsync(CancellationToken cancellationToken);

    Task<Result<MemberItem>> InviteAsync(InviteRequest request, CancellationToken cancellationToken);

    Task<Result<MemberItem>> SetRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken);

    Task<Result<MemberItem>> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SsoStatus>> SsoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GrantItem>> GrantsAsync(CancellationToken cancellationToken);

    Task<Result<GrantItem>> SaveGrantAsync(SaveGrantRequest request, CancellationToken cancellationToken);
}
