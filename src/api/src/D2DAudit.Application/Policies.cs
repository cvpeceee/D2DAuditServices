namespace D2DAudit.Application;

public sealed record PolicyListItem(
    string Code,
    string Title,
    string OwnerName,
    int Version,
    string State,
    IReadOnlyList<string> ControlCodes,
    int Acknowledged,
    int Outstanding);

public sealed record PolicyVersionDto(
    int Number,
    string State,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedByName);

public sealed record AcknowledgementDto(
    string PersonName,
    string PersonEmail,
    DateOnly DueOn,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? RemindedAt,
    bool ReminderDue);

public sealed record PolicyDetail(
    string Code,
    string Title,
    string OwnerName,
    IReadOnlyList<string> ControlCodes,
    IReadOnlyList<PolicyVersionDto> Versions,
    IReadOnlyList<AcknowledgementDto> Acknowledgements);

public sealed record AssignAcknowledgementRequest(string PersonName, string PersonEmail, DateOnly DueOn);

public interface IPolicyPortal
{
    Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> GetAsync(string code, CancellationToken cancellationToken);

    Task<Result<int>> InstallLibraryAsync(CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> DraftAsync(string code, CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> SubmitAsync(string code, CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> ApproveAsync(string code, CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> AssignAcknowledgementAsync(string code, AssignAcknowledgementRequest request, CancellationToken cancellationToken);

    Task<Result<PolicyDetail>> AcknowledgeAsync(string code, CancellationToken cancellationToken);

    Task<Result<int>> RemindAsync(CancellationToken cancellationToken);
}
