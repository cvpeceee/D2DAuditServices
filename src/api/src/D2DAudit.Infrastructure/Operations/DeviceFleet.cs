using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Operations;

public sealed class DeviceFleet(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    IMdmImport mdm,
    TimeProvider time) : IDeviceFleet
{
    public async Task<Result<ComputerItem>> CheckInAsync(AgentCheckInRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ComputerItem>.Fail("forbidden");
        }

        var serial = (request.Serial ?? "").Trim();
        var system = (request.OperatingSystem ?? "").Trim();
        var owner = (request.OwnerName ?? "").Trim();
        var email = (request.OwnerEmail ?? "").Trim();
        if (serial.Length is 0 or > 80 || system.Length is 0 or > 80 || owner.Length > 200 || email.Length > 320)
        {
            return Result<ComputerItem>.Fail("invalid_computer");
        }

        await using var db = Open();
        if (await db.DeviceCredentials.AnyAsync(item => item.Serial == serial && item.RevokedAt == null, cancellationToken))
        {
            // Enrolled devices only accept signed check-ins from their own agent credential.
            return Result<ComputerItem>.Fail("device_enrolled");
        }

        var row = await db.Computers.FirstOrDefaultAsync(item => item.Serial == serial, cancellationToken);
        if (row is null)
        {
            row = new ComputerRecord { Id = Guid.NewGuid(), Serial = serial };
            db.Computers.Add(row);
        }

        var now = time.GetUtcNow();
        row.OperatingSystem = system;
        row.OwnerName = owner;
        row.OwnerEmail = email;
        row.Encrypted = request.Encrypted;
        row.ScreenLock = request.ScreenLock;
        row.Antivirus = request.Antivirus;
        row.PasswordManager = request.PasswordManager;
        row.AutoUpdate = request.AutoUpdate;
        row.LastCheckIn = now;
        row.Source = "Agent";
        var checks = DevicePosture.FromComputer(row);
        db.DevicePostureSnapshots.Add(new DevicePostureSnapshot
        {
            Id = Guid.NewGuid(),
            Serial = serial,
            At = now,
            AgentKind = "legacy",
            AgentVersion = "",
            Platform = "",
            Passing = checks.Count(item => item.Pass),
            Failing = checks.Count(item => !item.Pass),
            ChecksJson = DevicePosture.Serialize(checks),
        });
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "computer.checkin", "ok", serial, null, cancellationToken);
        return Result<ComputerItem>.Ok(ToItem(row));
    }

    public async Task<Result<MdmSyncResult>> SyncAsync(CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<MdmSyncResult>.Fail("forbidden");
        }

        var reports = await mdm.ListAsync(cancellationToken);
        await using var db = Open();
        var imported = 0;
        var now = time.GetUtcNow();
        foreach (var report in reports)
        {
            var serial = report.Serial.Trim();
            if (serial.Length is 0 or > 80)
            {
                continue;
            }

            var row = await db.Computers.FirstOrDefaultAsync(item => item.Serial == serial, cancellationToken);
            if (row is null)
            {
                row = new ComputerRecord { Id = Guid.NewGuid(), Serial = serial };
                db.Computers.Add(row);
                imported++;
            }

            row.OperatingSystem = Trim(report.OperatingSystem, 80);
            row.OwnerName = Trim(report.OwnerName, 200);
            row.OwnerEmail = Trim(report.OwnerEmail, 320);
            row.Encrypted = report.Encrypted;
            row.ScreenLock = report.ScreenLock;
            row.Antivirus = report.Antivirus;
            row.PasswordManager = report.PasswordManager;
            row.AutoUpdate = report.AutoUpdate;
            row.LastCheckIn = now;
            row.Source = "Mdm";
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "computer.mdm", "ok", imported.ToString(), null, cancellationToken);
        return Result<MdmSyncResult>.Ok(new MdmSyncResult(imported));
    }

    private static string Trim(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static ComputerItem ToItem(ComputerRecord row) => new(
        row.Id,
        row.Serial,
        row.OperatingSystem,
        row.OwnerName,
        row.OwnerEmail,
        row.Encrypted,
        row.ScreenLock,
        row.Antivirus,
        row.PasswordManager,
        row.AutoUpdate,
        row.LastCheckIn,
        row.Source,
        row.ScreenshotName,
        row.EvidenceId);

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
