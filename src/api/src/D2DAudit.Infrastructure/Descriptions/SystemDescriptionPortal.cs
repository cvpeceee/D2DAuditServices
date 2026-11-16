using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Descriptions;

public sealed class SystemDescriptionPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : ISystemDescription
{
    public async Task<SystemDescriptionDto> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await LoadOrCreateAsync(db, cancellationToken);
        return ToDto(row);
    }

    public async Task<Result<SystemDescriptionDto>> SaveAsync(SaveSystemDescriptionRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<SystemDescriptionDto>.Fail("forbidden");
        }

        var body = (request.Body ?? "").Trim();
        if (body.Length is 0 or > 20000)
        {
            return Result<SystemDescriptionDto>.Fail("invalid_description");
        }

        await using var db = Open();
        var row = await LoadOrCreateAsync(db, cancellationToken);
        row.Body = body;
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedBy = tenant.DisplayName;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "description.saved", "ok", null, null, cancellationToken);
        return Result<SystemDescriptionDto>.Ok(ToDto(row));
    }

    public async Task<Result<SystemDescriptionDto>> RedraftAsync(CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<SystemDescriptionDto>.Fail("forbidden");
        }

        await using var db = Open();
        var row = await LoadOrCreateAsync(db, cancellationToken);
        row.Body = await SystemDescriptionText.DraftAsync(db, tenant.TenantName, tenant.Region, SystemDescriptionText.Product(tenant.IsExample), cancellationToken);
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedBy = tenant.DisplayName;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "description.redrafted", "ok", null, null, cancellationToken);
        return Result<SystemDescriptionDto>.Ok(ToDto(row));
    }

    private async Task<SystemDescription> LoadOrCreateAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.SystemDescriptions.FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new SystemDescription
        {
            Id = Guid.NewGuid(),
            Body = await SystemDescriptionText.DraftAsync(db, tenant.TenantName, tenant.Region, SystemDescriptionText.Product(tenant.IsExample), cancellationToken),
            UpdatedAt = time.GetUtcNow(),
            UpdatedBy = "Draft",
        };
        db.SystemDescriptions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static SystemDescriptionDto ToDto(SystemDescription row) => new(row.Body, row.UpdatedAt, row.UpdatedBy);

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
