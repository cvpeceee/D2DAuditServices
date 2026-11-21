using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Connection lifecycle for catalog connectors: connect, test, sync, disconnect and history.</summary>
public sealed class ConnectorHub(
    TenantContext tenant,
    TenantDatabases databases,
    ConnectorExecutor executor,
    ICollectionSchedule schedule,
    IActivityWriter activity,
    IConfiguration configuration,
    TimeProvider time) : IConnectorHub
{
    private const int HistoryLimit = 20;
    private const string Connected = "connected";
    private const string MockedHint = "Mocked until configured";
    private static readonly string[] RowBuiltins = ["aws", "azure", "gcp", "google", "microsoft", "github"];

    public async Task<ConnectorCatalogDto> CatalogAsync(string? category, string? search, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        var state = await StateAsync(db, cancellationToken);
        var term = search?.Trim() ?? "";
        var all = ConnectorCatalog.Connectors.Select(descriptor => Summary(descriptor, state)).ToArray();
        var filtered = all
            .Where(item => string.IsNullOrWhiteSpace(category) || string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(item => term.Length == 0 || Matches(item, term))
            .OrderBy(item => item.Status == Connected ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ConnectorCatalogDto(ConnectorCatalog.Categories, all.Length, all.Count(item => item.Status == Connected), filtered);
    }

    public async Task<Result<ConnectorDetailDto>> DetailAsync(string key, CancellationToken cancellationToken)
    {
        var descriptor = ConnectorCatalog.Find(key);
        if (descriptor is null)
        {
            return Result<ConnectorDetailDto>.Fail("not_found");
        }

        await using var db = databases.Create(Database());
        return Result<ConnectorDetailDto>.Ok(await DetailAsync(db, descriptor, cancellationToken));
    }

    public async Task<Result<ConnectorActionResponse>> ConnectAsync(string key, ConnectConnectorRequest request, CancellationToken cancellationToken)
    {
        var guard = Guard(key, out var descriptor);
        if (guard is not null)
        {
            return Result<ConnectorActionResponse>.Fail(guard);
        }

        await using var db = databases.Create(Database());
        var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == descriptor!.Key, cancellationToken);
        var stored = executor.ReadConfig(row);
        var config = ConnectorCredentials.Merge(descriptor!, stored.IsSuccess ? stored.Value : null, request.Config);
        var example = tenant.IsExample || row?.Example == true;
        var mode = ConnectorExecutor.Mode(descriptor!, config, example);
        if (mode == ConnectorModes.Mock && !example && !executor.MocksAllowed)
        {
            return Result<ConnectorActionResponse>.Fail("missing_fields");
        }

        if (mode == ConnectorModes.Live)
        {
            var started = time.GetUtcNow();
            var tested = await executor.TestAsync(descriptor!, config, example, cancellationToken);
            if (!tested.Result.IsSuccess)
            {
                if (row is not null)
                {
                    Record(db, descriptor!.Key, "test", tested.Mode, tested.Result.Error, 0, started, "manual");
                    await db.SaveChangesAsync(cancellationToken);
                }

                await LogAsync("integration.connector.connected", "failed", descriptor!, tested.Result.Error, cancellationToken);
                return Result<ConnectorActionResponse>.Fail("connection_test_failed");
            }
        }

        string? sealedConfig = null;
        if (!tenant.IsExample)
        {
            var protectedConfig = executor.Protect(config);
            if (!protectedConfig.IsSuccess)
            {
                return Result<ConnectorActionResponse>.Fail(protectedConfig.Error!);
            }

            sealedConfig = protectedConfig.Value;
        }

        if (row is null)
        {
            row = new IntegrationConnection { Id = Guid.NewGuid(), Provider = descriptor!.Key };
            db.Integrations.Add(row);
        }

        row.DisplayName = descriptor!.Name;
        row.Status = Connected;
        row.Example = tenant.IsExample;
        row.Secret = sealedConfig;
        row.LastError = null;
        row.SetupHint = mode == ConnectorModes.Mock ? MockedHint : null;
        row.AccountLabel = Label(descriptor, config);
        await db.SaveChangesAsync(cancellationToken);

        var run = await SyncCoreAsync(db, descriptor, row, config, "connect", cancellationToken);
        var job = await schedule.StartAsync(tenant.TenantId, cancellationToken);
        await LogAsync("integration.connector.connected", "ok", descriptor, mode, cancellationToken);
        var detail = await DetailAsync(db, descriptor, cancellationToken);
        return Result<ConnectorActionResponse>.Ok(new ConnectorActionResponse(detail, run, job.IsSuccess ? job.Value : null, mode));
    }

    public async Task<Result<ConnectorActionResponse>> TestAsync(string key, ConnectConnectorRequest? request, CancellationToken cancellationToken)
    {
        var guard = Guard(key, out var descriptor);
        if (guard is not null)
        {
            return Result<ConnectorActionResponse>.Fail(guard);
        }

        await using var db = databases.Create(Database());
        var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == descriptor!.Key, cancellationToken);
        var stored = executor.ReadConfig(row);
        if (!stored.IsSuccess)
        {
            return Result<ConnectorActionResponse>.Fail(stored.Error!);
        }

        var config = ConnectorCredentials.Merge(descriptor!, stored.Value, request?.Config);
        var example = tenant.IsExample || row?.Example == true;
        var started = time.GetUtcNow();
        var tested = await executor.TestAsync(descriptor!, config, example, cancellationToken);
        ConnectorRunDto? run = null;
        if (row is not null)
        {
            var entity = Record(db, descriptor!.Key, "test", tested.Mode, tested.Result.Error, 0, started, "manual");
            await db.SaveChangesAsync(cancellationToken);
            run = ToDto(entity);
        }

        await LogAsync("integration.connector.tested", tested.Result.IsSuccess ? "ok" : "failed", descriptor!, tested.Mode, cancellationToken);
        var detail = await DetailAsync(db, descriptor!, cancellationToken);
        return Result<ConnectorActionResponse>.Ok(new ConnectorActionResponse(
            detail,
            run ?? new ConnectorRunDto(Guid.Empty, "test", tested.Mode, tested.Result.IsSuccess ? "ok" : "failed", 0, tested.Result.Error, started, time.GetUtcNow(), "manual"),
            null,
            tested.Result.IsSuccess ? "ok" : tested.Result.Error));
    }

    public async Task<Result<ConnectorActionResponse>> SyncAsync(string key, CancellationToken cancellationToken)
    {
        var guard = Guard(key, out var descriptor);
        if (guard is not null)
        {
            return Result<ConnectorActionResponse>.Fail(guard);
        }

        await using var db = databases.Create(Database());
        var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == descriptor!.Key && item.Status == Connected, cancellationToken);
        if (row is null)
        {
            return Result<ConnectorActionResponse>.Fail("not_connected");
        }

        var stored = executor.ReadConfig(row);
        if (!stored.IsSuccess)
        {
            return Result<ConnectorActionResponse>.Fail(stored.Error!);
        }

        var run = await SyncCoreAsync(db, descriptor!, row, stored.Value!, "manual", cancellationToken);
        var job = await schedule.StartAsync(tenant.TenantId, cancellationToken);
        await LogAsync("integration.connector.synced", run.Status, descriptor!, run.Mode, cancellationToken);
        var detail = await DetailAsync(db, descriptor!, cancellationToken);
        return Result<ConnectorActionResponse>.Ok(new ConnectorActionResponse(detail, run, job.IsSuccess ? job.Value : null, run.Error));
    }

    public async Task<Result<ConnectorDetailDto>> DisconnectAsync(string key, CancellationToken cancellationToken)
    {
        var guard = Guard(key, out var descriptor);
        if (guard is not null)
        {
            return Result<ConnectorDetailDto>.Fail(guard);
        }

        await using var db = databases.Create(Database());
        var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == descriptor!.Key, cancellationToken);
        if (row is null)
        {
            return Result<ConnectorDetailDto>.Fail("not_connected");
        }

        db.Integrations.Remove(row);
        db.Resources.RemoveRange(await db.Resources.Where(item => item.Provider == descriptor!.Key).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await schedule.StartAsync(tenant.TenantId, cancellationToken);
        await LogAsync("integration.connector.disconnected", "ok", descriptor!, null, cancellationToken);
        return Result<ConnectorDetailDto>.Ok(await DetailAsync(db, descriptor!, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<ConnectorRunDto>>> HistoryAsync(string key, CancellationToken cancellationToken)
    {
        if (ConnectorCatalog.Find(key) is null)
        {
            return Result<IReadOnlyList<ConnectorRunDto>>.Fail("not_found");
        }

        await using var db = databases.Create(Database());
        return Result<IReadOnlyList<ConnectorRunDto>>.Ok(await HistoryAsync(db, key, cancellationToken));
    }

    private async Task<ConnectorRunDto> SyncCoreAsync(
        TenantDbContext db,
        ConnectorDescriptor descriptor,
        IntegrationConnection row,
        IReadOnlyDictionary<string, string> config,
        string trigger,
        CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        var outcome = await executor.CollectAsync(descriptor, config, row.Example || tenant.IsExample, cancellationToken);
        var count = 0;
        if (outcome.Result.IsSuccess && outcome.Result.Value is not null)
        {
            var existing = await db.Resources.Where(item => item.Provider == descriptor.Key).ToListAsync(cancellationToken);
            db.Resources.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var resource in outcome.Result.Value)
            {
                db.Resources.Add(new ResourceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Provider = descriptor.Key,
                    Kind = resource.Kind,
                    ExternalId = resource.ExternalId,
                    Name = resource.Name,
                    AttributesJson = JsonSerializer.Serialize(resource.Attributes),
                    CollectedAt = started,
                });
            }

            count = outcome.Result.Value.Count;
            row.LastError = null;
            row.LastCollectedAt = started;
        }
        else
        {
            row.LastError = outcome.Result.Error;
        }

        row.SetupHint = outcome.Mode == ConnectorModes.Mock ? MockedHint : null;
        var run = Record(db, descriptor.Key, "sync", outcome.Mode, outcome.Result.Error, count, started, trigger);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(run);
    }

    private IntegrationSyncRun Record(
        TenantDbContext db,
        string provider,
        string kind,
        string mode,
        string? error,
        int count,
        DateTimeOffset started,
        string trigger)
    {
        var run = new IntegrationSyncRun
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Kind = kind,
            Mode = mode,
            Status = error is null ? "ok" : "failed",
            ResourceCount = count,
            Error = error?[..Math.Min(error.Length, 300)],
            Trigger = trigger,
            StartedAt = started,
            FinishedAt = time.GetUtcNow(),
        };
        db.IntegrationSyncRuns.Add(run);
        return run;
    }

    private async Task<ConnectorDetailDto> DetailAsync(TenantDbContext db, ConnectorDescriptor descriptor, CancellationToken cancellationToken)
    {
        var state = await StateAsync(db, cancellationToken);
        var row = state.Rows.GetValueOrDefault(descriptor.Key);
        var config = executor.Registry.Handles(descriptor.Key) ? executor.ReadConfig(row) : default;
        var values = ConnectorCredentials.Redact(descriptor, config.IsSuccess && config.Value is not null ? config.Value : new Dictionary<string, string>());
        var tests = await db.ControlTests.AsNoTracking()
            .Where(item => descriptor.Tests.Contains(item.TestKey))
            .Select(item => new { item.TestKey, item.Outcome, item.Detail, item.ControlId })
            .ToListAsync(cancellationToken);
        var controlIds = tests.Select(item => item.ControlId).Distinct().ToArray();
        var codes = await db.Controls.AsNoTracking()
            .Where(item => controlIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);
        var checks = descriptor.Tests
            .Select(testKey =>
            {
                var spec = AutomatedChecks.All.FirstOrDefault(item => item.Key == testKey);
                var test = tests.FirstOrDefault(item => item.TestKey == testKey);
                var controlCode = test is not null && codes.TryGetValue(test.ControlId, out var code) ? code : spec?.ControlCode ?? "";
                return new ConnectorCheckDto(
                    testKey,
                    spec?.Name ?? testKey,
                    controlCode,
                    test is null ? "notRun" : JsonNamingPolicy.CamelCase.ConvertName(test.Outcome.ToString()),
                    test?.Detail ?? "");
            })
            .ToArray();
        var history = await HistoryAsync(db, descriptor.Key, cancellationToken);
        return new ConnectorDetailDto(Summary(descriptor, state), descriptor.Fields, values, descriptor.EnvKeys, checks, history);
    }

    private static async Task<IReadOnlyList<ConnectorRunDto>> HistoryAsync(TenantDbContext db, string key, CancellationToken cancellationToken)
    {
        var runs = await db.IntegrationSyncRuns.AsNoTracking()
            .Where(item => item.Provider == key)
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(item => item.StartedAt).Take(HistoryLimit).Select(ToDto).ToArray();
    }

    private async Task<CatalogState> StateAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Integrations.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.Resources.AsNoTracking()
            .GroupBy(item => item.Provider)
            .Select(group => new { Provider = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return new CatalogState(
            rows.GroupBy(item => item.Provider, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
            counts.ToDictionary(item => item.Provider, item => item.Count, StringComparer.Ordinal));
    }

    private ConnectorSummaryDto Summary(ConnectorDescriptor descriptor, CatalogState state)
    {
        var row = state.Rows.GetValueOrDefault(descriptor.Key);
        var count = state.Counts.GetValueOrDefault(descriptor.Key);
        string status;
        string? mode = null;
        if (descriptor.Builtin && !RowBuiltins.Contains(descriptor.Key))
        {
            var configured = descriptor.EnvKeys.Count > 0 && descriptor.EnvKeys.All(env => !string.IsNullOrWhiteSpace(configuration[env]));
            status = configured || tenant.IsExample ? Connected : "available";
            if (configured)
            {
                mode = ConnectorModes.Live;
            }
            else if (tenant.IsExample)
            {
                mode = ConnectorModes.Mock;
            }
        }
        else if (row is null || row.Status != Connected)
        {
            status = row is null ? "available" : row.Status;
        }
        else
        {
            status = string.IsNullOrEmpty(row.LastError) ? Connected : "error";
            var mocked = row.Example || (!descriptor.Builtin && row.SetupHint == MockedHint);
            mode = mocked ? ConnectorModes.Mock : ConnectorModes.Live;
        }

        return new ConnectorSummaryDto(
            descriptor.Key,
            descriptor.Name,
            descriptor.Category,
            descriptor.AuthType,
            descriptor.Description,
            descriptor.Resources,
            descriptor.Tests,
            descriptor.Controls,
            descriptor.DocsUrl,
            descriptor.Builtin,
            descriptor.ManagedAt,
            status,
            mode,
            count,
            row?.LastCollectedAt,
            row?.LastError);
    }

    private string? Guard(string key, out ConnectorDescriptor? descriptor)
    {
        descriptor = ConnectorCatalog.Find(key);
        if (descriptor is null)
        {
            return "not_found";
        }

        if (!tenant.CanWrite)
        {
            return "forbidden";
        }

        return descriptor.Builtin ? "managed_elsewhere" : null;
    }

    private static bool Matches(ConnectorSummaryDto item, string term) =>
        item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Resources.Any(resource => resource.Contains(term, StringComparison.OrdinalIgnoreCase))
        || item.Tests.Any(test => test.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string? Label(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config)
    {
        var field = descriptor.Fields.FirstOrDefault(item => !item.Secret && config.ContainsKey(item.Key));
        if (field is null)
        {
            return null;
        }

        var value = config[field.Key];
        return value.Length > 120 ? value[..120] : value;
    }

    private static ConnectorRunDto ToDto(IntegrationSyncRun run) =>
        new(run.Id, run.Kind, run.Mode, run.Status, run.ResourceCount, run.Error, run.StartedAt, run.FinishedAt, run.Trigger);

    private Task LogAsync(string action, string result, ConnectorDescriptor descriptor, string? detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(
            tenant.UserId,
            tenant.TenantId,
            action,
            result,
            detail is null ? descriptor.Key : descriptor.Key + " (" + detail + ")",
            null,
            cancellationToken);

    private string Database()
    {
        if (!tenant.IsResolved || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return tenant.DatabaseName;
    }

    private sealed record CatalogState(IReadOnlyDictionary<string, IntegrationConnection> Rows, IReadOnlyDictionary<string, int> Counts);
}
