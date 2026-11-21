using System.Globalization;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Operations;

public sealed class DeviceAgentService(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IActivityWriter activity,
    TimeProvider time) : IDeviceAgent
{
    public const string CheckInPath = "/api/v1/agent/check-in";
    public const int IntervalHours = 6;

    public async Task<Result<EnrollmentTokenCreated>> CreateTokenAsync(CreateEnrollmentTokenRequest request, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<EnrollmentTokenCreated>.Fail("forbidden");
        }

        var email = string.IsNullOrWhiteSpace(request.PersonEmail) ? tenant.Email : request.PersonEmail.Trim();
        var self = string.Equals(email, tenant.Email, StringComparison.OrdinalIgnoreCase);
        if (!self && !tenant.CanWrite)
        {
            return Result<EnrollmentTokenCreated>.Fail("forbidden");
        }

        if (email.Length is 0 or > 320 || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result<EnrollmentTokenCreated>.Fail("invalid_person");
        }

        await using var db = Open();
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(item => EF.Functions.ILike(item.Email, email), cancellationToken);
        if (person is null && !self)
        {
            return Result<EnrollmentTokenCreated>.Fail("invalid_person");
        }

        var now = time.GetUtcNow();
        var raw = DeviceAgentTokens.CreateEnrollmentToken(tenant.TenantId);
        var row = new DeviceEnrollmentToken
        {
            Id = Guid.NewGuid(),
            PersonEmail = person?.Email ?? email,
            PersonName = person?.Name ?? tenant.DisplayName,
            TokenHash = DeviceAgentTokens.Hash(raw),
            Hint = DeviceAgentTokens.Hint(raw),
            CreatedByEmail = tenant.Email,
            CreatedAt = now,
            ExpiresAt = now.Add(DeviceAgentTokens.EnrollmentLifetime),
        };
        db.DeviceEnrollmentTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "agent.token.created", "ok", row.PersonEmail, null, cancellationToken);
        return Result<EnrollmentTokenCreated>.Ok(new EnrollmentTokenCreated(row.Id, raw, row.PersonEmail, row.PersonName, row.ExpiresAt));
    }

    public async Task<IReadOnlyList<EnrollmentTokenItem>> TokensAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var query = db.DeviceEnrollmentTokens.AsNoTracking();
        if (!tenant.CanWrite)
        {
            var email = tenant.Email;
            query = query.Where(item => EF.Functions.ILike(item.PersonEmail, email));
        }

        var now = time.GetUtcNow();
        var rows = await query.OrderByDescending(item => item.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return rows.Select(item => ToItem(item, now)).ToArray();
    }

    public async Task<Result<EnrollmentTokenItem>> RevokeTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<EnrollmentTokenItem>.Fail("forbidden");
        }

        await using var db = Open();
        var row = await db.DeviceEnrollmentTokens.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<EnrollmentTokenItem>.Fail("not_found");
        }

        if (!tenant.CanWrite && !string.Equals(row.PersonEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<EnrollmentTokenItem>.Fail("forbidden");
        }

        var now = time.GetUtcNow();
        row.RevokedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "agent.token.revoked", "ok", row.PersonEmail, null, cancellationToken);
        return Result<EnrollmentTokenItem>.Ok(ToItem(row, now));
    }

    public async Task<IReadOnlyList<AgentDeviceItem>> DevicesAsync(bool mine, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var query = db.DeviceCredentials.AsNoTracking();
        if (mine || !tenant.CanWrite)
        {
            var email = tenant.Email;
            query = query.Where(item => EF.Functions.ILike(item.PersonEmail, email));
        }

        var rows = await query.OrderBy(item => item.PersonName).ThenBy(item => item.Serial).ToListAsync(cancellationToken);
        var ids = rows.Select(item => (Guid?)item.Id).ToArray();
        var snapshots = await LatestAsync(db, ids, cancellationToken);
        var now = time.GetUtcNow();
        return rows.Select(row => ToDevice(row, snapshots.GetValueOrDefault(row.Id), now)).ToArray();
    }

    public async Task<Result<IReadOnlyList<PostureHistoryItem>>> HistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<IReadOnlyList<PostureHistoryItem>>.Fail("forbidden");
        }

        await using var db = Open();
        var row = await db.DeviceCredentials.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<IReadOnlyList<PostureHistoryItem>>.Fail("not_found");
        }

        if (!tenant.CanWrite && !string.Equals(row.PersonEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<IReadOnlyList<PostureHistoryItem>>.Fail("forbidden");
        }

        var history = await db.DevicePostureSnapshots.AsNoTracking()
            .Where(item => item.DeviceId == id)
            .OrderByDescending(item => item.At)
            .Take(60)
            .ToListAsync(cancellationToken);
        IReadOnlyList<PostureHistoryItem> items = history
            .Select(item => new PostureHistoryItem(item.At, item.AgentKind, item.AgentVersion, item.Passing, item.Failing, Views(DevicePosture.Deserialize(item.ChecksJson), row.Platform)))
            .ToArray();
        return Result<IReadOnlyList<PostureHistoryItem>>.Ok(items);
    }

    public async Task<Result<AgentDeviceItem>> RevokeDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<AgentDeviceItem>.Fail("forbidden");
        }

        await using var db = Open();
        var row = await db.DeviceCredentials.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<AgentDeviceItem>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        row.RevokedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestsAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "agent.device.revoked", "ok", row.Serial, null, cancellationToken);
        var latest = await LatestAsync(db, [row.Id], cancellationToken);
        return Result<AgentDeviceItem>.Ok(ToDevice(row, latest.GetValueOrDefault(row.Id), now));
    }

    public async Task<AgentCall<AgentEnrollResult>> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
    {
        if (!DeviceAgentTokens.TryParseEnrollmentToken(request.Token, out var tenantId))
        {
            return AgentCall<AgentEnrollResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        var serial = (request.Serial ?? "").Trim();
        var system = (request.OperatingSystem ?? "").Trim();
        var platform = (request.Platform ?? "").Trim().ToLowerInvariant();
        var kind = string.IsNullOrWhiteSpace(request.AgentKind) ? "dotnet" : request.AgentKind.Trim().ToLowerInvariant();
        if (serial.Length is 0 or > 80 || system.Length is 0 or > 80 || !DevicePosture.Platforms.Contains(platform) || !DevicePosture.AgentKinds.Contains(kind))
        {
            return AgentCall<AgentEnrollResult>.Fail("invalid_device");
        }

        var databaseName = await DatabaseAsync(tenantId, cancellationToken);
        if (databaseName is null)
        {
            return AgentCall<AgentEnrollResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        await using var db = databases.Create(databaseName);
        var hash = DeviceAgentTokens.Hash(request.Token);
        var token = await db.DeviceEnrollmentTokens.FirstOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (token is null || !DeviceAgentTokens.HashEquals(token.TokenHash, hash))
        {
            return AgentCall<AgentEnrollResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        if (!DeviceAgentTokens.IsUsable(token, now))
        {
            return AgentCall<AgentEnrollResult>.Denied(AgentAuthFailure.Expired);
        }

        var previous = await db.DeviceCredentials.Where(item => item.Serial == serial && item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var item in previous)
        {
            item.RevokedAt = now;
        }

        var secret = DeviceAgentTokens.CreateSecret();
        var credential = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            Serial = serial,
            Hostname = Clip(request.Hostname, 120),
            Platform = platform,
            OperatingSystem = system,
            AgentKind = kind,
            AgentVersion = Clip(request.AgentVersion, 32),
            PersonEmail = token.PersonEmail,
            PersonName = token.PersonName,
            SecretHash = DeviceSignature.KeyFromSecret(secret),
            CreatedAt = now,
        };
        db.DeviceCredentials.Add(credential);
        token.UsedAt = now;
        token.DeviceId = credential.Id;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, tenantId, "agent.enrolled", "ok", serial, null, cancellationToken);
        return AgentCall<AgentEnrollResult>.Ok(new AgentEnrollResult(DeviceAgentTokens.DeviceId(tenantId, credential.Id), secret, CheckInPath, IntervalHours, credential.PersonEmail));
    }

    public async Task<AgentCall<AgentCheckInResult>> CheckInAsync(string? deviceId, string? timestamp, string? signature, string body, CancellationToken cancellationToken)
    {
        if (!DeviceAgentTokens.TryParseDeviceId(deviceId, out var tenantId, out var credentialId))
        {
            return AgentCall<AgentCheckInResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        var databaseName = await DatabaseAsync(tenantId, cancellationToken);
        if (databaseName is null)
        {
            return AgentCall<AgentCheckInResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        await using var db = databases.Create(databaseName);
        var credential = await db.DeviceCredentials.FirstOrDefaultAsync(item => item.Id == credentialId && item.RevokedAt == null, cancellationToken);
        if (credential is null)
        {
            return AgentCall<AgentCheckInResult>.Denied(AgentAuthFailure.Unauthorized);
        }

        var now = time.GetUtcNow();
        var verdict = DeviceSignature.Verify(credential.SecretHash, timestamp, body, signature, now.ToUnixTimeSeconds(), credential.LastSignedTimestamp);
        if (verdict != SignatureCheck.Valid)
        {
            await activity.WriteAsync(null, tenantId, "agent.checkin", "denied", credential.Serial + " " + verdict, null, cancellationToken);
            return AgentCall<AgentCheckInResult>.Denied(verdict switch
            {
                SignatureCheck.Expired => AgentAuthFailure.Expired,
                SignatureCheck.Replayed => AgentAuthFailure.Replayed,
                SignatureCheck.Invalid => AgentAuthFailure.Invalid,
                _ => AgentAuthFailure.Unauthorized,
            });
        }

        credential.LastSignedTimestamp = long.Parse(timestamp!, NumberStyles.None, CultureInfo.InvariantCulture);
        var (payload, error) = DevicePosture.Parse(body);
        if (payload is null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return AgentCall<AgentCheckInResult>.Fail(error ?? "invalid_payload");
        }

        if (!string.Equals(payload.Serial.Trim(), credential.Serial, StringComparison.Ordinal))
        {
            await db.SaveChangesAsync(cancellationToken);
            return AgentCall<AgentCheckInResult>.Fail("serial_mismatch");
        }

        var computer = await db.Computers.FirstOrDefaultAsync(item => item.Serial == credential.Serial, cancellationToken);
        if (computer is null)
        {
            computer = new ComputerRecord { Id = Guid.NewGuid(), Serial = credential.Serial };
            db.Computers.Add(computer);
        }

        computer.OperatingSystem = Clip(payload.OperatingSystem, 80);
        computer.OwnerName = credential.PersonName;
        computer.OwnerEmail = credential.PersonEmail;
        computer.LastCheckIn = now;
        computer.Source = "Agent";
        DevicePosture.Apply(computer, payload.Checks);

        credential.LastSeenAt = now;
        credential.AgentKind = payload.AgentKind;
        credential.AgentVersion = Clip(payload.AgentVersion, 32);
        credential.OperatingSystem = computer.OperatingSystem;
        credential.Hostname = Clip(payload.Hostname, 120);
        var passing = payload.Checks.Count(item => item.Pass);
        db.DevicePostureSnapshots.Add(new DevicePostureSnapshot
        {
            Id = Guid.NewGuid(),
            DeviceId = credential.Id,
            Serial = credential.Serial,
            At = now,
            AgentKind = payload.AgentKind,
            AgentVersion = credential.AgentVersion,
            Platform = payload.Platform,
            Passing = passing,
            Failing = payload.Checks.Count - passing,
            ChecksJson = DevicePosture.Serialize(payload.Checks),
        });

        var person = await db.People.FirstOrDefaultAsync(item => EF.Functions.ILike(item.Email, credential.PersonEmail), cancellationToken);
        if (person is not null)
        {
            person.DeviceStatus = "Reporting";
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestsAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, tenantId, "agent.checkin", "ok", credential.Serial, null, cancellationToken);
        return AgentCall<AgentCheckInResult>.Ok(new AgentCheckInResult(credential.Serial, passing, payload.Checks.Count - passing, now, IntervalHours));
    }

    internal static async Task RefreshTestsAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var devices = await db.DeviceCredentials.AsNoTracking().Where(item => item.RevokedAt == null).ToListAsync(cancellationToken);
        var latest = await LatestAsync(db, devices.Select(item => (Guid?)item.Id).ToArray(), cancellationToken);
        var fresh = devices
            .Where(item => !DevicePosture.IsStale(item.LastSeenAt, now) && latest.ContainsKey(item.Id))
            .Select(item => (item.Serial, DevicePosture.Deserialize(latest[item.Id].ChecksJson)))
            .ToArray();
        var codes = DevicePosture.Checks.Select(item => item.ControlCode).Distinct(StringComparer.Ordinal).ToArray();
        var controls = await db.Controls.Include(item => item.Tests).Where(item => codes.Contains(item.Code)).ToListAsync(cancellationToken);

        foreach (var info in DevicePosture.Checks)
        {
            var controlRow = controls.FirstOrDefault(item => item.Code == info.ControlCode);
            if (controlRow is not null)
            {
                var (outcome, detail) = DevicePosture.CheckOutcome(info.Key, fresh);
                Upsert(db, controlRow, DevicePosture.TestKey(info.Key), DevicePosture.TestName(info), outcome, detail, "Run the D2D device agent and fix the failing check using the steps on the employee's /me device card.");
            }
        }

        var encryption = controls.FirstOrDefault(item => item.Code == "EP-03");
        if (encryption is not null)
        {
            var (outcome, detail) = DevicePosture.StaleOutcome(devices.Select(item => (item.Serial, item.LastSeenAt)), now);
            Upsert(db, encryption, DevicePosture.StaleTestKey, DevicePosture.StaleTestName, outcome, detail, "Ask the device owner to reconnect the laptop or reinstall the agent; revoke devices that were retired.");
        }
    }

    private static void Upsert(TenantDbContext db, Control controlRow, string key, string name, TestOutcome outcome, string detail, string remediation)
    {
        var test = controlRow.Tests.FirstOrDefault(item => item.TestKey == key);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlRow.Id,
                Name = name,
                Kind = TestKind.Automated,
                TestKey = key,
            };
            controlRow.Tests.Add(test);
            db.ControlTests.Add(test);
        }

        test.Outcome = outcome;
        test.Detail = detail;
        test.Remediation = remediation;
        if (outcome == TestOutcome.Fail)
        {
            controlRow.Status = ControlStatus.Failing;
        }
    }

    private static async Task<Dictionary<Guid, DevicePostureSnapshot>> LatestAsync(TenantDbContext db, IReadOnlyCollection<Guid?> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.DevicePostureSnapshots.AsNoTracking()
            .Where(item => ids.Contains(item.DeviceId))
            .GroupBy(item => item.DeviceId)
            .Select(group => group.OrderByDescending(item => item.At).First())
            .ToListAsync(cancellationToken);
        return rows.Where(item => item.DeviceId is not null).ToDictionary(item => item.DeviceId!.Value);
    }

    private async Task<string?> DatabaseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var row = await control.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenantId && item.Status == TenantStatus.Active && item.DatabaseName != null, cancellationToken);
        return row?.DatabaseName is { } name && DatabaseNames.IsSafe(name) ? name : null;
    }

    private static AgentDeviceItem ToDevice(DeviceCredential row, DevicePostureSnapshot? snapshot, DateTimeOffset now)
    {
        var checks = snapshot is null ? [] : Views(DevicePosture.Deserialize(snapshot.ChecksJson), row.Platform);
        return new AgentDeviceItem(
            row.Id,
            row.Serial,
            row.Hostname,
            row.Platform,
            row.OperatingSystem,
            row.PersonEmail,
            row.PersonName,
            row.AgentKind,
            row.AgentVersion,
            row.CreatedAt,
            row.LastSeenAt,
            row.RevokedAt is null && DevicePosture.IsStale(row.LastSeenAt, now),
            row.RevokedAt is not null,
            checks.Count(item => item.Pass),
            checks.Count(item => !item.Pass),
            checks);
    }

    internal static IReadOnlyList<AgentCheckView> Views(IReadOnlyList<PostureCheck> checks, string platform) =>
        checks.Select(check =>
        {
            var info = DevicePosture.Info(check.Key);
            return new AgentCheckView(
                check.Key,
                info?.Title ?? check.Key,
                info?.ControlCode ?? "",
                check.Pass,
                check.Detail,
                check.Pass ? [] : DevicePosture.Remediation(check.Key, platform));
        }).ToArray();

    private static EnrollmentTokenItem ToItem(DeviceEnrollmentToken row, DateTimeOffset now) =>
        new(row.Id, row.Hint, row.PersonEmail, row.PersonName, row.CreatedByEmail, row.CreatedAt, row.ExpiresAt, DeviceAgentTokens.State(row, now));

    private static string Clip(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);
}
