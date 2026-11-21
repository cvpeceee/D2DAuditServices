using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

internal static class AssuranceScope
{
    public static string Database(TenantContext tenant)
    {
        if (!tenant.IsResolved || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return tenant.DatabaseName;
    }

    public static async Task<Dictionary<string, Control>> ControlsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Tests)
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return controls.ToDictionary(item => item.Code, StringComparer.Ordinal);
    }

    public static bool InScope(TenantContext tenant, Control control) =>
        !tenant.IsAuditor || control.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal));

    public static string Actor(TenantContext tenant) => string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Email : tenant.DisplayName;
}
