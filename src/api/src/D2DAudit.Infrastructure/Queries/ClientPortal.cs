using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Queries;

public sealed class ClientPortal(TenantContext tenant, TenantDatabases databases) : IClientRead
{
    public async Task<IReadOnlyList<HistoryPoint>> HistoryAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        return await db.CheckWeeks.AsNoTracking()
            .OrderBy(item => item.WeekOf)
            .Select(item => new HistoryPoint(item.WeekOf, item.Passing, item.Failing))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> DeviceCountAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        return await db.Devices.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDto>> NotificationsAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        return await db.Notifications.AsNoTracking()
            .OrderByDescending(item => item.At)
            .Select(item => new NotificationDto(item.Title, item.Body, item.Href, item.At))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceDto>> DevicesAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        return await db.Devices.AsNoTracking()
            .OrderBy(item => item.OwnerName)
            .ThenBy(item => item.Serial)
            .Select(item => new DeviceDto(item.OwnerName, item.Platform, item.Serial, item.LastSeenOn))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResourceDto>> ResourcesAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        var rows = await db.Resources.AsNoTracking()
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(item => new ResourceDto(item.Provider, item.Kind, item.Name, Detail(item.AttributesJson))).ToArray();
    }

    private string RequiredDatabase()
    {
        if (!tenant.IsResolved || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not available.");
        }

        return tenant.DatabaseName;
    }

    private static string Detail(string attributesJson)
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(attributesJson) ?? [];
        var parts = new List<string>();
        foreach (var key in new[] { "email", "status", "mfa", "public", "encrypted", "protected", "enabled", "offboarded" })
        {
            if (attributes.TryGetValue(key, out var value) && value.Length > 0)
            {
                parts.Add(key + " " + value);
            }
        }

        return string.Join(", ", parts);
    }
}
