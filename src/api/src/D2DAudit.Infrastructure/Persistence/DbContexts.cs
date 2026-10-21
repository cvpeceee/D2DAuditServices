using D2DAudit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace D2DAudit.Infrastructure.Persistence;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<AuditorGrant> AuditorGrants => Set<AuditorGrant>();

    public DbSet<AuditorGrantFramework> AuditorGrantFrameworks => Set<AuditorGrantFramework>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public DbSet<ActivityEvent> Activity => Set<ActivityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<Tenant>();
        tenant.ToTable("tenants", table => table.HasCheckConstraint("ck_tenant_retention_min", "\"RetentionYears\" >= 3"));
        tenant.HasKey(x => x.Id);
        tenant.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        tenant.Property(x => x.Name).HasMaxLength(200).IsRequired();
        tenant.Property(x => x.Region).HasMaxLength(8).IsRequired();
        tenant.Property(x => x.DatabaseName).HasMaxLength(63);
        tenant.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        tenant.HasIndex(x => x.Slug).IsUnique();

        var user = modelBuilder.Entity<UserAccount>();
        user.ToTable("users");
        user.HasKey(x => x.Id);
        user.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        user.Property(x => x.Email).HasMaxLength(320).IsRequired();
        user.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        user.Property(x => x.PasswordHash).HasMaxLength(500);
        user.HasIndex(x => x.Subject).IsUnique();
        user.HasIndex(x => x.Email).IsUnique();

        var membership = modelBuilder.Entity<Membership>();
        membership.ToTable("memberships");
        membership.HasKey(x => x.Id);
        membership.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        membership.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
        membership.HasOne(x => x.User).WithMany(x => x.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        membership.HasOne(x => x.Tenant).WithMany(x => x.Memberships).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);

        var grant = modelBuilder.Entity<AuditorGrant>();
        grant.ToTable("auditor_grants");
        grant.HasKey(x => x.Id);
        grant.Property(x => x.AuditName).HasMaxLength(200).IsRequired();
        grant.Property(x => x.FirmName).HasMaxLength(200).IsRequired();
        grant.HasIndex(x => new { x.UserId, x.TenantId });

        var grantFramework = modelBuilder.Entity<AuditorGrantFramework>();
        grantFramework.ToTable("auditor_grant_frameworks");
        grantFramework.HasKey(x => new { x.GrantId, x.FrameworkKey });
        grantFramework.Property(x => x.FrameworkKey).HasMaxLength(64);
        grantFramework.HasOne(x => x.Grant).WithMany(x => x.Frameworks).HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Cascade);

        var domain = modelBuilder.Entity<TenantDomain>();
        domain.ToTable("tenant_domains");
        domain.HasKey(x => x.Id);
        domain.Property(x => x.Domain).HasMaxLength(253).IsRequired();
        domain.Property(x => x.WorkOsOrganizationId).HasMaxLength(64);
        domain.HasIndex(x => x.Domain).IsUnique();

        var job = modelBuilder.Entity<JobRecord>();
        job.ToTable("jobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        job.Property(x => x.CurrentStep).HasMaxLength(200).IsRequired();
        job.Property(x => x.Error).HasMaxLength(500);
        job.HasIndex(x => x.TenantId);

        var activity = modelBuilder.Entity<ActivityEvent>();
        activity.ToTable("activity_events");
        activity.HasKey(x => x.Id);
        activity.Property(x => x.Action).HasMaxLength(64).IsRequired();
        activity.Property(x => x.Result).HasMaxLength(32).IsRequired();
        activity.Property(x => x.Detail).HasMaxLength(500);
        activity.Property(x => x.IpAddress).HasMaxLength(64);
        activity.HasIndex(x => new { x.TenantId, x.At });
    }
}

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<FrameworkRecord> Frameworks => Set<FrameworkRecord>();

    public DbSet<Requirement> Requirements => Set<Requirement>();

    public DbSet<Control> Controls => Set<Control>();

    public DbSet<ControlRequirementMap> ControlMappings => Set<ControlRequirementMap>();

    public DbSet<ControlTest> ControlTests => Set<ControlTest>();

    public DbSet<EvidenceItem> Evidence => Set<EvidenceItem>();

    public DbSet<AuditEngagement> Audits => Set<AuditEngagement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var framework = modelBuilder.Entity<FrameworkRecord>();
        framework.ToTable("frameworks");
        framework.HasKey(x => x.Id);
        framework.Property(x => x.Key).HasMaxLength(64).IsRequired();
        framework.Property(x => x.Name).HasMaxLength(200).IsRequired();
        framework.Property(x => x.Version).HasMaxLength(32).IsRequired();
        framework.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        framework.HasIndex(x => x.Key).IsUnique();

        var requirement = modelBuilder.Entity<Requirement>();
        requirement.ToTable("requirements");
        requirement.HasKey(x => x.Id);
        requirement.Property(x => x.Code).HasMaxLength(64).IsRequired();
        requirement.Property(x => x.Title).HasMaxLength(300).IsRequired();
        requirement.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
        requirement.HasIndex(x => new { x.FrameworkId, x.Code }).IsUnique();
        requirement.HasOne(x => x.Framework).WithMany(x => x.Requirements).HasForeignKey(x => x.FrameworkId).OnDelete(DeleteBehavior.Cascade);
        requirement.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        var control = modelBuilder.Entity<Control>();
        control.ToTable("controls");
        control.HasKey(x => x.Id);
        control.Property(x => x.Code).HasMaxLength(32).IsRequired();
        control.Property(x => x.Title).HasMaxLength(300).IsRequired();
        control.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        control.Property(x => x.OwnerName).HasMaxLength(200);
        control.Property(x => x.OwnerEmail).HasMaxLength(320);
        control.Property(x => x.Frequency).HasMaxLength(64).IsRequired();
        control.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        control.HasIndex(x => x.Code).IsUnique();

        var map = modelBuilder.Entity<ControlRequirementMap>();
        map.ToTable("control_requirements");
        map.HasKey(x => new { x.ControlId, x.RequirementId });
        map.Property(x => x.Coverage).HasConversion<string>().HasMaxLength(16);
        map.HasOne(x => x.Control).WithMany(x => x.Mappings).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Cascade);
        map.HasOne(x => x.Requirement).WithMany(x => x.Mappings).HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);

        var test = modelBuilder.Entity<ControlTest>();
        test.ToTable("control_tests");
        test.HasKey(x => x.Id);
        test.Property(x => x.Name).HasMaxLength(300).IsRequired();
        test.Property(x => x.Detail).HasMaxLength(2000);
        test.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        test.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32);
        test.HasOne<Control>().WithMany(x => x.Tests).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Cascade);

        var evidence = modelBuilder.Entity<EvidenceItem>();
        evidence.ToTable("evidence_items");
        evidence.HasKey(x => x.Id);
        evidence.Property(x => x.Title).HasMaxLength(300).IsRequired();
        evidence.Property(x => x.Source).HasMaxLength(200).IsRequired();
        evidence.Property(x => x.Sha256).HasMaxLength(64);
        evidence.Property(x => x.Integrity).HasConversion<string>().HasMaxLength(32);
        evidence.HasOne<Control>().WithMany(x => x.Evidence).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Cascade);

        var audit = modelBuilder.Entity<AuditEngagement>();
        audit.ToTable("audit_engagements");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Name).HasMaxLength(200).IsRequired();
        audit.Property(x => x.FirmName).HasMaxLength(200).IsRequired();
        audit.Property(x => x.FrameworkKey).HasMaxLength(64).IsRequired();
    }
}

public sealed class ControlPlaneDesignTimeFactory : IDesignTimeDbContextFactory<ControlPlaneDbContext>
{
    public ControlPlaneDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONTROL_PLANE_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=d2d_control;Username=d2d;Password=d2d_dev_password";
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>().UseNpgsql(cs).Options;
        return new ControlPlaneDbContext(options);
    }
}

public sealed class TenantDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TENANT_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=d2d_tenant_design;Username=d2d;Password=d2d_dev_password";
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(cs).Options;
        return new TenantDbContext(options);
    }
}
