using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Audits;

internal static class SystemDescriptionText
{
    public static async Task<string> CurrentAsync(
        TenantDbContext db,
        string company,
        string region,
        string product,
        CancellationToken cancellationToken)
    {
        var row = await db.SystemDescriptions.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (row is not null && row.Body.Length > 0)
        {
            return row.Body;
        }

        return await DraftAsync(db, company, region, product, cancellationToken);
    }

    public static async Task<string> DraftAsync(
        TenantDbContext db,
        string company,
        string region,
        string product,
        CancellationToken cancellationToken)
    {
        var frameworks = await db.Frameworks.AsNoTracking().OrderBy(item => item.Name).Select(item => item.Name).ToListAsync(cancellationToken);
        var controls = await db.Controls.AsNoTracking().CountAsync(cancellationToken);
        var passing = await db.Controls.AsNoTracking().CountAsync(item => item.Status == ControlStatus.Passing, cancellationToken);
        var services = await db.Integrations.AsNoTracking().OrderBy(item => item.DisplayName).Select(item => item.DisplayName).ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking().CountAsync(cancellationToken);
        var vendors = await db.Vendors.AsNoTracking().CountAsync(cancellationToken);
        return SystemDescriptionDraft.Compose(company, region, product, frameworks, controls, passing, services, people, vendors);
    }

    public static string Product(bool example) => example ? ClientSeed.Company.Summary : "";
}
