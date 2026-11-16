namespace D2DAudit.Application;

public sealed record ChangeItem(
    Guid Id,
    string Code,
    string Title,
    string Source,
    string ExternalKey,
    string Repository,
    string AuthorName,
    string Status,
    string ApproverName,
    DateTimeOffset? ApprovedAt,
    string DeployRef,
    DateTimeOffset? DeployedAt,
    DateTimeOffset OpenedAt,
    bool Reviewed);

public sealed record RecordChangeRequest(string Title, string Source, string ExternalKey, string Repository);

public sealed record UpdateChangeRequest(string Action, string? DeployRef);

public sealed record ChangeSyncResult(int Imported);

public sealed record ExternalChange(
    string Source,
    string ExternalKey,
    string Title,
    string Repository,
    string AuthorName,
    bool Approved,
    string ApproverName,
    DateTimeOffset? DeployedAt,
    string DeployRef);

public interface IJiraChangeFeed
{
    Task<IReadOnlyList<ExternalChange>> ListAsync(CancellationToken cancellationToken);
}

public interface IChanges
{
    Task<IReadOnlyList<ChangeItem>> ListAsync(CancellationToken cancellationToken);

    Task<Result<ChangeItem>> RecordAsync(RecordChangeRequest request, CancellationToken cancellationToken);

    Task<Result<ChangeItem>> UpdateAsync(Guid id, UpdateChangeRequest request, CancellationToken cancellationToken);

    Task<Result<ChangeSyncResult>> SyncAsync(CancellationToken cancellationToken);
}
