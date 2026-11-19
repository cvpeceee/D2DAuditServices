using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace D2DAudit.Tests;

public sealed class SchemaParityTests
{
    [Fact]
    public void ControlPlane_and_Tenant_migrations_have_no_pending_model_changes()
    {
        using var control = new ControlPlaneDesignTimeFactory().CreateDbContext([]);
        Assert.False(control.Database.HasPendingModelChanges(), "ControlPlaneDbContext has pending model changes without a migration.");

        using var tenant = new TenantDesignTimeFactory().CreateDbContext([]);
        Assert.False(tenant.Database.HasPendingModelChanges(), "TenantDbContext has pending model changes without a migration.");

        var controlMigrations = control.GetService<IMigrationsAssembly>().Migrations;
        var tenantMigrations = tenant.GetService<IMigrationsAssembly>().Migrations;
        Assert.NotEmpty(controlMigrations);
        Assert.NotEmpty(tenantMigrations);
    }

    [Fact]
    public void Auditor_export_package_keys_are_isolated_from_admin_packages()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var auditorId = Guid.NewGuid();
        var adminKey = AuditPortal.PackageKey(tenantId, jobId, null);
        var auditorKey = AuditPortal.PackageKey(tenantId, jobId, auditorId);

        Assert.NotEqual(adminKey, auditorKey);
        Assert.Contains("/exports/auditor-", auditorKey, StringComparison.Ordinal);
        Assert.DoesNotContain("/exports/auditor-", adminKey, StringComparison.Ordinal);
    }
}
