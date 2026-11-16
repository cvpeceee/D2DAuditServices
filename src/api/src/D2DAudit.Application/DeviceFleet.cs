namespace D2DAudit.Application;

public sealed record DeviceReport(
    string Serial,
    string OperatingSystem,
    string OwnerName,
    string OwnerEmail,
    bool Encrypted,
    bool ScreenLock,
    bool Antivirus,
    bool PasswordManager,
    bool AutoUpdate);

public sealed record AgentCheckInRequest(
    string Serial,
    string OperatingSystem,
    string OwnerName,
    string OwnerEmail,
    bool Encrypted,
    bool ScreenLock,
    bool Antivirus,
    bool PasswordManager,
    bool AutoUpdate);

public sealed record MdmSyncResult(int Imported);

public interface IMdmImport
{
    Task<IReadOnlyList<DeviceReport>> ListAsync(CancellationToken cancellationToken);
}

public interface IDeviceFleet
{
    Task<Result<ComputerItem>> CheckInAsync(AgentCheckInRequest request, CancellationToken cancellationToken);

    Task<Result<MdmSyncResult>> SyncAsync(CancellationToken cancellationToken);
}
