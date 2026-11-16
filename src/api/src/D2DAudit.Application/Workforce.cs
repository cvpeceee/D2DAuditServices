namespace D2DAudit.Application;

public sealed record DirectoryImport(string Name, string Email, string Department, string Role, bool Active);

public sealed record ImportCount(int Imported);

public interface IWorkforceImport
{
    Task<Result<ImportCount>> OktaAsync(CancellationToken cancellationToken);

    Task<Result<ImportCount>> HrisAsync(CancellationToken cancellationToken);
}
