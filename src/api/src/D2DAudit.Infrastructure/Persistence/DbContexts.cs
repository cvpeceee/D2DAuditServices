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

    public DbSet<PolicyDocument> Policies => Set<PolicyDocument>();

    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();

    public DbSet<PolicyControlLink> PolicyControls => Set<PolicyControlLink>();

    public DbSet<PolicyAcknowledgement> Acknowledgements => Set<PolicyAcknowledgement>();

    public DbSet<SampleRequest> SampleRequests => Set<SampleRequest>();

    public DbSet<SampleResponse> SampleResponses => Set<SampleResponse>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<IntegrationConnection> Integrations => Set<IntegrationConnection>();

    public DbSet<ChainEntry> Chain => Set<ChainEntry>();

    public DbSet<ResourceSnapshot> Resources => Set<ResourceSnapshot>();

    public DbSet<RiskItem> Risks => Set<RiskItem>();

    public DbSet<RiskControlLink> RiskControls => Set<RiskControlLink>();

    public DbSet<DirectoryPerson> People => Set<DirectoryPerson>();

    public DbSet<PersonTask> PersonTasks => Set<PersonTask>();

    public DbSet<PersonAccount> PersonAccounts => Set<PersonAccount>();

    public DbSet<SoaEntry> Soa => Set<SoaEntry>();

    public DbSet<VendorRecord> Vendors => Set<VendorRecord>();

    public DbSet<VendorDocument> VendorDocuments => Set<VendorDocument>();

    public DbSet<AccessCampaign> AccessCampaigns => Set<AccessCampaign>();

    public DbSet<AccessReviewItem> AccessItems => Set<AccessReviewItem>();

    public DbSet<CheckWeek> CheckWeeks => Set<CheckWeek>();

    public DbSet<WorkspaceNotification> Notifications => Set<WorkspaceNotification>();

    public DbSet<EnrolledDevice> Devices => Set<EnrolledDevice>();

    public DbSet<FindingComment> FindingComments => Set<FindingComment>();

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
        test.Property(x => x.TestKey).HasMaxLength(64);
        test.Property(x => x.Remediation).HasMaxLength(2000);
        test.Property(x => x.Requirements).HasMaxLength(400);
        test.HasIndex(x => new { x.ControlId, x.TestKey });
        test.HasOne<Control>().WithMany(x => x.Tests).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Cascade);

        var evidence = modelBuilder.Entity<EvidenceItem>();
        evidence.ToTable("evidence_items");
        evidence.HasKey(x => x.Id);
        evidence.Property(x => x.Title).HasMaxLength(300).IsRequired();
        evidence.Property(x => x.Source).HasMaxLength(200).IsRequired();
        evidence.Property(x => x.Sha256).HasMaxLength(64);
        evidence.Property(x => x.ObjectKey).HasMaxLength(200);
        evidence.Property(x => x.PreviousHead).HasMaxLength(64);
        evidence.Property(x => x.ChainHead).HasMaxLength(64);
        evidence.Property(x => x.Integrity).HasConversion<string>().HasMaxLength(32);
        evidence.HasOne<Control>().WithMany(x => x.Evidence).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Cascade);

        var integration = modelBuilder.Entity<IntegrationConnection>();
        integration.ToTable("integration_connections");
        integration.HasKey(x => x.Id);
        integration.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        integration.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        integration.Property(x => x.Status).HasMaxLength(32).IsRequired();
        integration.Property(x => x.Fixture).HasMaxLength(32).IsRequired();
        integration.Property(x => x.ExternalId).HasMaxLength(80);
        integration.Property(x => x.RoleArn).HasMaxLength(300);
        integration.Property(x => x.InstallationId).HasMaxLength(40);
        integration.Property(x => x.AccountLabel).HasMaxLength(200);
        integration.Property(x => x.Domain).HasMaxLength(253);
        integration.Property(x => x.Secret).HasMaxLength(4000);
        integration.Property(x => x.SetupHint).HasMaxLength(500);
        integration.Property(x => x.LastError).HasMaxLength(300);
        integration.HasIndex(x => x.Provider).IsUnique();

        var chain = modelBuilder.Entity<ChainEntry>();
        chain.ToTable("evidence_chain");
        chain.HasKey(x => x.Id);
        chain.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
        chain.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        chain.Property(x => x.PreviousHead).HasMaxLength(64);
        chain.Property(x => x.Head).HasMaxLength(64).IsRequired();
        chain.Property(x => x.Canonical).HasMaxLength(600).IsRequired();
        chain.HasIndex(x => x.Sequence).IsUnique();

        var snapshot = modelBuilder.Entity<ResourceSnapshot>();
        snapshot.ToTable("resource_snapshots");
        snapshot.HasKey(x => x.Id);
        snapshot.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        snapshot.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        snapshot.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
        snapshot.Property(x => x.Name).HasMaxLength(300).IsRequired();
        snapshot.Property(x => x.AttributesJson).HasMaxLength(4000).IsRequired();
        snapshot.HasIndex(x => new { x.Provider, x.Kind, x.ExternalId }).IsUnique();

        var audit = modelBuilder.Entity<AuditEngagement>();
        audit.ToTable("audit_engagements");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Name).HasMaxLength(200).IsRequired();
        audit.Property(x => x.FirmName).HasMaxLength(200).IsRequired();
        audit.Property(x => x.FrameworkKey).HasMaxLength(64).IsRequired();
        audit.Property(x => x.Scope).HasMaxLength(1000).IsRequired();

        var policy = modelBuilder.Entity<PolicyDocument>();
        policy.ToTable("policies");
        policy.HasKey(x => x.Id);
        policy.Property(x => x.Code).HasMaxLength(32).IsRequired();
        policy.Property(x => x.Title).HasMaxLength(200).IsRequired();
        policy.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        policy.HasIndex(x => x.Code).IsUnique();

        var version = modelBuilder.Entity<PolicyVersion>();
        version.ToTable("policy_versions");
        version.HasKey(x => x.Id);
        version.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        version.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        version.Property(x => x.ApprovedByName).HasMaxLength(200);
        version.HasIndex(x => new { x.PolicyId, x.Number }).IsUnique();
        version.HasOne(x => x.Policy).WithMany(x => x.Versions).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);

        var link = modelBuilder.Entity<PolicyControlLink>();
        link.ToTable("policy_controls");
        link.HasKey(x => new { x.PolicyId, x.ControlCode });
        link.Property(x => x.ControlCode).HasMaxLength(32);
        link.HasOne(x => x.Policy).WithMany(x => x.Controls).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);

        var acknowledgement = modelBuilder.Entity<PolicyAcknowledgement>();
        acknowledgement.ToTable("policy_acknowledgements");
        acknowledgement.HasKey(x => x.Id);
        acknowledgement.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        acknowledgement.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        acknowledgement.HasIndex(x => new { x.PolicyVersionId, x.PersonEmail }).IsUnique();
        acknowledgement.HasOne(x => x.Version).WithMany(x => x.Acknowledgements).HasForeignKey(x => x.PolicyVersionId).OnDelete(DeleteBehavior.Cascade);

        var sample = modelBuilder.Entity<SampleRequest>();
        sample.ToTable("sample_requests");
        sample.HasKey(x => x.Id);
        sample.Property(x => x.Code).HasMaxLength(32).IsRequired();
        sample.Property(x => x.Title).HasMaxLength(200).IsRequired();
        sample.Property(x => x.Population).HasMaxLength(200).IsRequired();
        sample.Property(x => x.Source).HasMaxLength(100).IsRequired();
        sample.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        sample.HasIndex(x => x.Code).IsUnique();

        var response = modelBuilder.Entity<SampleResponse>();
        response.ToTable("sample_responses");
        response.HasKey(x => x.Id);
        response.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        response.Property(x => x.RespondedByName).HasMaxLength(200).IsRequired();
        response.HasIndex(x => x.RequestId).IsUnique();
        response.HasOne(x => x.Request).WithOne(x => x.Response).HasForeignKey<SampleResponse>(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);

        var finding = modelBuilder.Entity<Finding>();
        finding.ToTable("findings");
        finding.HasKey(x => x.Id);
        finding.Property(x => x.Code).HasMaxLength(32).IsRequired();
        finding.Property(x => x.Title).HasMaxLength(200).IsRequired();
        finding.Property(x => x.Detail).HasMaxLength(2000).IsRequired();
        finding.Property(x => x.RequirementCode).HasMaxLength(64).IsRequired();
        finding.Property(x => x.ManagementResponse).HasMaxLength(2000);
        finding.Property(x => x.ResponseByName).HasMaxLength(200);
        finding.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
        finding.HasIndex(x => x.Code).IsUnique();

        var risk = modelBuilder.Entity<RiskItem>();
        risk.ToTable("risks");
        risk.HasKey(x => x.Id);
        risk.Property(x => x.Code).HasMaxLength(32).IsRequired();
        risk.Property(x => x.Title).HasMaxLength(200).IsRequired();
        risk.Property(x => x.Statement).HasMaxLength(2000).IsRequired();
        risk.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        risk.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        risk.Property(x => x.Treatment).HasConversion<string>().HasMaxLength(32);
        risk.Property(x => x.TreatmentPlan).HasMaxLength(2000).IsRequired();
        risk.HasIndex(x => x.Code).IsUnique();

        var riskControl = modelBuilder.Entity<RiskControlLink>();
        riskControl.ToTable("risk_controls");
        riskControl.HasKey(x => new { x.RiskId, x.ControlCode });
        riskControl.Property(x => x.ControlCode).HasMaxLength(32);
        riskControl.HasOne(x => x.Risk).WithMany(x => x.Controls).HasForeignKey(x => x.RiskId).OnDelete(DeleteBehavior.Cascade);

        var person = modelBuilder.Entity<DirectoryPerson>();
        person.ToTable("people");
        person.HasKey(x => x.Id);
        person.Property(x => x.Name).HasMaxLength(200).IsRequired();
        person.Property(x => x.Email).HasMaxLength(320).IsRequired();
        person.Property(x => x.Department).HasMaxLength(200).IsRequired();
        person.Property(x => x.Role).HasMaxLength(200).IsRequired();
        person.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        person.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        person.Property(x => x.Training).HasConversion<string>().HasMaxLength(32);
        person.HasIndex(x => x.Email).IsUnique();

        var task = modelBuilder.Entity<PersonTask>();
        task.ToTable("person_tasks");
        task.HasKey(x => x.Id);
        task.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        task.Property(x => x.TaskCode).HasMaxLength(32).IsRequired();
        task.HasIndex(x => new { x.PersonId, x.TaskCode }).IsUnique();
        task.HasOne(x => x.Person).WithMany(x => x.Tasks).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);

        var account = modelBuilder.Entity<PersonAccount>();
        account.ToTable("person_accounts");
        account.HasKey(x => x.Id);
        account.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        account.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
        account.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
        account.HasIndex(x => new { x.PersonId, x.Provider, x.ExternalId }).IsUnique();
        account.HasOne(x => x.Person).WithMany(x => x.Accounts).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);

        var soa = modelBuilder.Entity<SoaEntry>();
        soa.ToTable("soa_entries");
        soa.HasKey(x => x.Id);
        soa.Property(x => x.Code).HasMaxLength(32).IsRequired();
        soa.Property(x => x.Title).HasMaxLength(300).IsRequired();
        soa.Property(x => x.Justification).HasMaxLength(2000).IsRequired();
        soa.Property(x => x.Implementation).HasConversion<string>().HasMaxLength(32);
        soa.HasIndex(x => x.Code).IsUnique();

        var vendor = modelBuilder.Entity<VendorRecord>();
        vendor.ToTable("vendors");
        vendor.HasKey(x => x.Id);
        vendor.Property(x => x.Name).HasMaxLength(200).IsRequired();
        vendor.Property(x => x.Service).HasMaxLength(200).IsRequired();
        vendor.Property(x => x.Criticality).HasConversion<string>().HasMaxLength(32);
        vendor.Property(x => x.DataAccess).HasMaxLength(1000).IsRequired();
        vendor.Property(x => x.ReviewState).HasConversion<string>().HasMaxLength(32);

        var document = modelBuilder.Entity<VendorDocument>();
        document.ToTable("vendor_documents");
        document.HasKey(x => x.Id);
        document.Property(x => x.Title).HasMaxLength(300).IsRequired();
        document.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        document.HasOne(x => x.Vendor).WithMany(x => x.Documents).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);

        var campaign = modelBuilder.Entity<AccessCampaign>();
        campaign.ToTable("access_campaigns");
        campaign.HasKey(x => x.Id);
        campaign.Property(x => x.Name).HasMaxLength(200).IsRequired();
        campaign.Property(x => x.State).HasConversion<string>().HasMaxLength(32);

        var reviewItem = modelBuilder.Entity<AccessReviewItem>();
        reviewItem.ToTable("access_review_items");
        reviewItem.HasKey(x => x.Id);
        reviewItem.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        reviewItem.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        reviewItem.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        reviewItem.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
        reviewItem.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        reviewItem.Property(x => x.ReviewerName).HasMaxLength(200);
        reviewItem.HasOne(x => x.Campaign).WithMany(x => x.Items).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);

        var week = modelBuilder.Entity<CheckWeek>();
        week.ToTable("check_weeks");
        week.HasKey(x => x.Id);
        week.HasIndex(x => x.WeekOf).IsUnique();

        var note = modelBuilder.Entity<WorkspaceNotification>();
        note.ToTable("notifications");
        note.HasKey(x => x.Id);
        note.Property(x => x.Title).HasMaxLength(200).IsRequired();
        note.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        note.Property(x => x.Href).HasMaxLength(200).IsRequired();

        var device = modelBuilder.Entity<EnrolledDevice>();
        device.ToTable("devices");
        device.HasKey(x => x.Id);
        device.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        device.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        device.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        device.Property(x => x.Serial).HasMaxLength(64).IsRequired();
        device.HasIndex(x => x.Serial).IsUnique();

        var comment = modelBuilder.Entity<FindingComment>();
        comment.ToTable("finding_comments");
        comment.HasKey(x => x.Id);
        comment.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        comment.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        comment.HasOne(x => x.Finding).WithMany(x => x.Comments).HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Cascade);
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
