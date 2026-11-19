using System.Security.Cryptography;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class IntegrationQuery(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    ProviderSet providers,
    ICollectionSchedule schedule,
    EvidenceWriter evidence,
    IStorageProvider storage,
    IConfiguration configuration,
    TimeProvider time) : IIntegrationQuery
{
    public async Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        var rows = await db.Integrations.AsNoTracking().OrderBy(item => item.Provider).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<Result<ConnectResponse>> ConnectAsync(
        string provider,
        ConnectIntegrationRequest request,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var key = provider.Trim().ToLowerInvariant();
        if (key is not ("aws" or "google" or "microsoft" or "github"))
        {
            return Result<ConnectResponse>.Fail("unknown_provider");
        }

        var allowedRedirects = (configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
            .Concat(configuration.GetSection("WorkOS:RedirectUris").Get<string[]>() ?? [])
            .ToArray();
        if (!RedirectPolicy.IsAllowed(redirectUri, allowedRedirects))
        {
            return Result<ConnectResponse>.Fail("redirect_not_allowed");
        }

        await using var db = databases.Create(Database());
        var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == key, cancellationToken);
        if (row is null)
        {
            row = new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                Provider = key,
                DisplayName = IntegrationPolicies.DisplayName(key),
                Status = "pending",
                Fixture = "",
            };
            db.Integrations.Add(row);
        }

        var isCallback = !string.IsNullOrWhiteSpace(request.Code) || !string.IsNullOrWhiteSpace(request.InstallationId);
        if (isCallback
            && (string.IsNullOrWhiteSpace(row.ExternalId)
                || string.IsNullOrWhiteSpace(request.State)
                || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(request.State.Trim()),
                    System.Text.Encoding.UTF8.GetBytes(row.ExternalId))))
        {
            return Result<ConnectResponse>.Fail("invalid_state");
        }

        string state;
        if (isCallback)
        {
            state = row.ExternalId!;
        }
        else if (key == "aws" && !string.IsNullOrWhiteSpace(row.ExternalId))
        {
            state = row.ExternalId;
        }
        else
        {
            state = RandomNumberGenerator.GetHexString(32, lowercase: true);
        }
        if (key != "aws" && !isCallback)
        {
            row.ExternalId = state;
        }

        var stamped = request with { State = state };
        var connected = await providers.ConnectAsync(key, providers.ToConnection(row), stamped, redirectUri, cancellationToken);
        if (!connected.IsSuccess || connected.Value is null)
        {
            return Result<ConnectResponse>.Fail(connected.Error ?? "connect_failed");
        }

        var error = providers.Apply(row, connected.Value);
        if (error is not null)
        {
            return Result<ConnectResponse>.Fail(error);
        }

        if (isCallback && key != "aws")
        {
            // Rotate the OAuth state token after successful completion so it cannot be replayed.
            row.ExternalId = RandomNumberGenerator.GetHexString(32, lowercase: true);
        }

        if (row.Example)
        {
            row.Status = "connected";
            row.Secret = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<ConnectResponse>.Ok(new ConnectResponse(
            ToDto(row),
            connected.Value.AuthorizationUrl,
            connected.Value.TrustPolicy,
            connected.Value.PermissionPolicy));
    }

    public Task<Result<Guid>> CollectAsync(CancellationToken cancellationToken) =>
        schedule.StartAsync(tenant.TenantId, cancellationToken);

    public async Task<Result<IReadOnlyList<IntegrationDto>>> UseExampleFixturesAsync(string fixture, CancellationToken cancellationToken)
    {
        if (!tenant.IsExample)
        {
            return Result<IReadOnlyList<IntegrationDto>>.Fail("forbidden");
        }

        if (fixture is not ("acme" or "northwind"))
        {
            return Result<IReadOnlyList<IntegrationDto>>.Fail("invalid_fixture");
        }

        await using var db = databases.Create(Database());
        foreach (var provider in IntegrationPolicies.ExampleProviders(fixture))
        {
            var row = await db.Integrations.FirstOrDefaultAsync(item => item.Provider == provider, cancellationToken);
            if (row is null)
            {
                row = new IntegrationConnection
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    DisplayName = IntegrationPolicies.DisplayName(provider),
                };
                db.Integrations.Add(row);
            }

            row.Status = "connected";
            row.Example = true;
            row.Fixture = fixture;
            row.Secret = null;
            row.LastError = null;
            row.AccountLabel = fixture == "northwind" ? "Northwind Labs example" : ClientSeed.Company.Name + " example";
            row.SetupHint = "Example fixture. No customer account is connected.";
        }

        await db.SaveChangesAsync(cancellationToken);
        var rows = await db.Integrations.AsNoTracking().OrderBy(item => item.Provider).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<IntegrationDto>>.Ok(rows.Select(ToDto).ToArray());
    }

    public async Task<IReadOnlyList<AutomatedTestDto>> TestsAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Tests)
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return controls
            .Where(InScope)
            .SelectMany(control => control.Tests.Where(test => test.TestKey is not null).Select(test => new AutomatedTestDto(
                test.Id,
                test.TestKey!,
                test.Name,
                control.Code,
                ApiName(test.Outcome),
                test.Detail,
                test.Remediation ?? "",
                Split(test.Requirements))))
            .OrderBy(item => item.ControlCode, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<EvidencePage> EvidenceAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Evidence)
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var chain = await db.Chain.AsNoTracking().OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var retention = await control.Tenants.AsNoTracking()
            .Where(item => item.Id == tenant.TenantId)
            .Select(item => item.RetentionYears)
            .FirstAsync(cancellationToken);
        var window = tenant.IsAuditor
            ? (await db.Audits.AsNoTracking().OrderByDescending(item => item.PeriodEnd).ToListAsync(cancellationToken))
                .FirstOrDefault(item => tenant.AuditorFrameworkKeys.Contains(item.FrameworkKey, StringComparer.Ordinal))
            : null;
        var items = controls
            .Where(InScope)
            .SelectMany(controlItem => controlItem.Evidence.Select(item => new EvidenceListItem(
                item.Id,
                item.Title,
                controlItem.Code,
                item.Source,
                item.CollectedAt,
                ApiName(item.Integrity),
                item.Sha256,
                item.LockedUntil,
                item.ObjectKey)))
            .Where(item => window is null || AuditWindow.Contains(window.PeriodStart, window.PeriodEnd, item.CollectedAt))
            .OrderByDescending(item => item.CollectedAt)
            .ToArray();
        var automated = items.Count(item => item.Source.StartsWith("Automated", StringComparison.Ordinal));
        var summary = new EvidenceSummary(items.Length, automated, items.Length - automated, items.Count(item => item.Integrity == "verified"), retention, ChainIntact(chain));
        return new EvidencePage(summary, items);
    }

    public async Task<VerifyReport> VerifyAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        return await evidence.VerifyAsync(db, tenant.TenantId, cancellationToken);
    }

    public async Task<Result<EvidenceListItem>> UploadAsync(UploadEvidenceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300 || string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return Result<EvidenceListItem>.Fail("invalid_evidence");
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return Result<EvidenceListItem>.Fail("invalid_evidence");
        }

        if (body.Length == 0 || body.Length > 750_000)
        {
            return Result<EvidenceListItem>.Fail("invalid_evidence");
        }

        await using var db = databases.Create(Database());
        var controlItem = await db.Controls
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .FirstOrDefaultAsync(item => item.Code == request.ControlCode, cancellationToken);
        if (controlItem is null || !InScope(controlItem))
        {
            return Result<EvidenceListItem>.Fail("not_found");
        }

        var retention = await control.Tenants.AsNoTracking()
            .Where(item => item.Id == tenant.TenantId)
            .Select(item => item.RetentionYears)
            .FirstAsync(cancellationToken);
        var id = Guid.NewGuid();
        var item = new EvidenceItem
        {
            Id = id,
            ControlId = controlItem.Id,
            Title = request.Title.Trim(),
            Source = "Manual",
            ObjectKey = tenant.TenantId.ToString("N") + "/" + id.ToString("N") + ".bin",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(item);
        await evidence.AppendAsync(db, tenant.TenantId, item, body, "application/octet-stream", time.GetUtcNow().AddYears(retention), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result<EvidenceListItem>.Ok(new EvidenceListItem(
            item.Id,
            item.Title,
            controlItem.Code,
            item.Source,
            item.CollectedAt,
            ApiName(item.Integrity),
            item.Sha256,
            item.LockedUntil,
            item.ObjectKey));
    }

    public async Task<Result<EvidenceDownload>> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(Database());
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Evidence)
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var match = controls
            .Where(InScope)
            .SelectMany(controlItem => controlItem.Evidence.Select(item => (Control: controlItem, Item: item)))
            .FirstOrDefault(pair => pair.Item.Id == id);
        if (match.Item is null || string.IsNullOrWhiteSpace(match.Item.ObjectKey))
        {
            return Result<EvidenceDownload>.Fail("not_found");
        }

        if (tenant.IsAuditor)
        {
            var window = (await db.Audits.AsNoTracking().OrderByDescending(item => item.PeriodEnd).ToListAsync(cancellationToken))
                .FirstOrDefault(item => tenant.AuditorFrameworkKeys.Contains(item.FrameworkKey, StringComparer.Ordinal));
            if (window is not null && !AuditWindow.Contains(window.PeriodStart, window.PeriodEnd, match.Item.CollectedAt))
            {
                return Result<EvidenceDownload>.Fail("not_found");
            }
        }

        if (!match.Item.ObjectKey.StartsWith(tenant.TenantId.ToString("N") + "/", StringComparison.Ordinal))
        {
            return Result<EvidenceDownload>.Fail("not_found");
        }

        byte[] body;
        try
        {
            body = await storage.GetAsync(match.Item.ObjectKey, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or Amazon.S3.AmazonS3Exception or InvalidOperationException)
        {
            return Result<EvidenceDownload>.Fail("not_found");
        }

        var parts = match.Item.ObjectKey.Split('/');
        var fileName = $"{match.Control.Code}-{parts[^1]}";
        var contentType = ContentTypeFor(fileName);
        return Result<EvidenceDownload>.Ok(new EvidenceDownload(body, fileName, contentType, match.Item.Sha256));
    }

    private static string ContentTypeFor(string fileName)
    {
        if (fileName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
        {
            return "application/vnd.apache.parquet";
        }

        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return "text/csv";
        }

        if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return "text/plain";
        }

        return "application/octet-stream";
    }

    private bool InScope(Control controlItem)
    {
        if (!tenant.IsAuditor)
        {
            return true;
        }

        return controlItem.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal));
    }

    private string Database()
    {
        if (!tenant.IsResolved || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return tenant.DatabaseName;
    }

    private static bool ChainIntact(IReadOnlyList<ChainEntry> entries)
    {
        string? previous = null;
        foreach (var entry in entries)
        {
            var head = EvidenceChain.NextHead(previous, entry.ContentHash, entry.Canonical);
            if (!string.Equals(head, entry.Head, StringComparison.Ordinal) || !string.Equals(previous, entry.PreviousHead, StringComparison.Ordinal))
            {
                return false;
            }

            previous = entry.Head;
        }

        return true;
    }

    private static IntegrationDto ToDto(IntegrationConnection row) =>
        new(
            row.Id,
            row.Provider,
            row.DisplayName,
            row.Status,
            row.Example,
            row.AccountLabel,
            row.ExternalId,
            row.RoleArn,
            row.Domain,
            row.SetupHint,
            row.LastCollectedAt);

    private static string[] Split(string? requirements) =>
        string.IsNullOrWhiteSpace(requirements)
            ? []
            : requirements.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ApiName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
