using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Descriptions;

internal static class SystemDescriptionExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.SystemDescriptions.Local.Any() || await db.SystemDescriptions.AnyAsync(cancellationToken))
        {
            return;
        }

        var company = ClientSeed.Company;
        db.SystemDescriptions.Add(new SystemDescription
        {
            Id = Guid.NewGuid(),
            Body = await SystemDescriptionText.DraftAsync(db, company.Name, company.Region, company.Summary, cancellationToken),
            UpdatedAt = now,
            UpdatedBy = "Draft",
        });
    }
}
