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

    public DbSet<ProviderClaim> ProviderClaims => Set<ProviderClaim>();

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
        membership.Property(x => x.Active).HasDefaultValue(true);
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

        var claim = modelBuilder.Entity<ProviderClaim>();
        claim.ToTable("provider_claims");
        claim.HasKey(x => x.Id);
        claim.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        claim.Property(x => x.ExternalAccountId).HasMaxLength(200).IsRequired();
        claim.HasIndex(x => new { x.Provider, x.ExternalAccountId }).IsUnique();
        claim.HasIndex(x => x.TenantId);

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

    public DbSet<TrackedAsset> Assets => Set<TrackedAsset>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

    public DbSet<TrainingModule> TrainingModules => Set<TrainingModule>();

    public DbSet<TrainingCompletion> TrainingCompletions => Set<TrainingCompletion>();

    public DbSet<ComputerRecord> Computers => Set<ComputerRecord>();

    public DbSet<VulnerabilityRecord> Vulnerabilities => Set<VulnerabilityRecord>();

    public DbSet<VulnerabilityPolicy> VulnerabilityPolicies => Set<VulnerabilityPolicy>();

    public DbSet<SlaEvent> SlaEvents => Set<SlaEvent>();

    public DbSet<TrustProfile> TrustProfiles => Set<TrustProfile>();

    public DbSet<TrustDocument> TrustDocuments => Set<TrustDocument>();

    public DbSet<TrustAccessRequest> TrustRequests => Set<TrustAccessRequest>();

    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();

    public DbSet<AutomationDelivery> AutomationDeliveries => Set<AutomationDelivery>();

    public DbSet<SecurityIncident> Incidents => Set<SecurityIncident>();

    public DbSet<IncidentEvent> IncidentEvents => Set<IncidentEvent>();

    public DbSet<ChangeRecord> Changes => Set<ChangeRecord>();

    public DbSet<SecurityQuestionnaire> Questionnaires => Set<SecurityQuestionnaire>();

    public DbSet<QuestionnaireAnswer> QuestionnaireAnswers => Set<QuestionnaireAnswer>();

    public DbSet<SystemDescription> SystemDescriptions => Set<SystemDescription>();

    public DbSet<ImpactAnalysis> ImpactAnalyses => Set<ImpactAnalysis>();

    public DbSet<ContinuityPlan> ContinuityPlans => Set<ContinuityPlan>();

    public DbSet<RecoveryExercise> RecoveryExercises => Set<RecoveryExercise>();

    public DbSet<PentestReport> PentestReports => Set<PentestReport>();

    public DbSet<VendorQuestion> VendorQuestions => Set<VendorQuestion>();

    public DbSet<ExposureFinding> ExposureFindings => Set<ExposureFinding>();

    public DbSet<SecurityScanFinding> SecurityScanFindings => Set<SecurityScanFinding>();

    public DbSet<DataStoreAsset> DataStoreAssets => Set<DataStoreAsset>();

    public DbSet<CostRecommendation> CostRecommendations => Set<CostRecommendation>();

    public DbSet<ApiKeyRecord> ApiKeys => Set<ApiKeyRecord>();

    public DbSet<WebhookSubscription> Webhooks => Set<WebhookSubscription>();

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
        control.Property(x => x.Custom).HasDefaultValue(false);
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
        risk.Property(x => x.ApprovedBy).HasMaxLength(200).HasDefaultValue("");
        risk.Property(x => x.LibraryCode).HasMaxLength(32).HasDefaultValue("");
        risk.Property(x => x.Approved).HasDefaultValue(false);
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
        person.Property(x => x.BackgroundStatus).HasMaxLength(32).HasDefaultValue("NotStarted");
        person.Property(x => x.BackgroundProvider).HasMaxLength(100).HasDefaultValue("");
        person.Property(x => x.BackgroundLink).HasMaxLength(500).HasDefaultValue("");
        person.Property(x => x.BackgroundExclusionReason).HasMaxLength(500).HasDefaultValue("");
        person.Property(x => x.BackgroundExcluded).HasDefaultValue(false);
        person.Property(x => x.DeviceStatus).HasMaxLength(32).HasDefaultValue("Missing");
        person.Property(x => x.StatusReason).HasMaxLength(500).HasDefaultValue("");
        person.Property(x => x.MfaEnrolled).HasDefaultValue(false);
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
        vendor.Property(x => x.Decision).HasMaxLength(32).HasDefaultValue("Pending");

        var document = modelBuilder.Entity<VendorDocument>();
        document.ToTable("vendor_documents");
        document.HasKey(x => x.Id);
        document.Property(x => x.Title).HasMaxLength(300).IsRequired();
        document.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        document.HasOne(x => x.Vendor).WithMany(x => x.Documents).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);

        var question = modelBuilder.Entity<VendorQuestion>();
        question.ToTable("vendor_questions");
        question.HasKey(x => x.Id);
        question.Property(x => x.Prompt).HasMaxLength(500).IsRequired();
        question.Property(x => x.Answer).HasMaxLength(2000).IsRequired();
        question.HasOne(x => x.Vendor).WithMany(x => x.Questions).HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);

        var campaign = modelBuilder.Entity<AccessCampaign>();
        campaign.ToTable("access_campaigns");
        campaign.HasKey(x => x.Id);
        campaign.Property(x => x.Name).HasMaxLength(200).IsRequired();
        campaign.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        campaign.Property(x => x.Providers).HasMaxLength(500).HasDefaultValue("");

        var reviewItem = modelBuilder.Entity<AccessReviewItem>();
        reviewItem.ToTable("access_review_items");
        reviewItem.HasKey(x => x.Id);
        reviewItem.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        reviewItem.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        reviewItem.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        reviewItem.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
        reviewItem.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        reviewItem.Property(x => x.ReviewerName).HasMaxLength(200);
        reviewItem.Property(x => x.RemediationNote).HasMaxLength(500).HasDefaultValue("");
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

        var asset = modelBuilder.Entity<TrackedAsset>();
        asset.ToTable("assets");
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Name).HasMaxLength(200).IsRequired();
        asset.Property(x => x.AssetType).HasMaxLength(32).IsRequired();
        asset.Property(x => x.Source).HasMaxLength(64).IsRequired();
        asset.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        asset.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        asset.Property(x => x.Environment).HasMaxLength(32).IsRequired();
        asset.Property(x => x.Classification).HasMaxLength(32).IsRequired();
        asset.Property(x => x.ExternalId).HasMaxLength(300).IsRequired();
        asset.HasIndex(x => x.ExternalId).IsUnique();

        var work = modelBuilder.Entity<WorkTask>();
        work.ToTable("work_tasks");
        work.HasKey(x => x.Id);
        work.Property(x => x.Title).HasMaxLength(200).IsRequired();
        work.Property(x => x.LinkedKind).HasMaxLength(32).IsRequired();
        work.Property(x => x.LinkedCode).HasMaxLength(64).IsRequired();
        work.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        work.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        work.Property(x => x.Status).HasMaxLength(32).IsRequired();
        work.Property(x => x.Recurrence).HasMaxLength(32).IsRequired();

        var module = modelBuilder.Entity<TrainingModule>();
        module.ToTable("training_modules");
        module.HasKey(x => x.Id);
        module.Property(x => x.Code).HasMaxLength(32).IsRequired();
        module.Property(x => x.Title).HasMaxLength(200).IsRequired();
        module.Property(x => x.ModuleType).HasMaxLength(32).IsRequired();
        module.Property(x => x.AssignedGroup).HasMaxLength(100).IsRequired();
        module.Property(x => x.ExternalUrl).HasMaxLength(500);
        module.HasIndex(x => x.Code).IsUnique();

        var completion = modelBuilder.Entity<TrainingCompletion>();
        completion.ToTable("training_completions");
        completion.HasKey(x => x.Id);
        completion.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        completion.HasIndex(x => new { x.ModuleId, x.PersonEmail }).IsUnique();
        completion.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);

        var computer = modelBuilder.Entity<ComputerRecord>();
        computer.ToTable("computers");
        computer.HasKey(x => x.Id);
        computer.Property(x => x.Serial).HasMaxLength(64).IsRequired();
        computer.Property(x => x.OperatingSystem).HasMaxLength(80).IsRequired();
        computer.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        computer.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        computer.Property(x => x.Source).HasMaxLength(16).IsRequired();
        computer.Property(x => x.ScreenshotName).HasMaxLength(200);
        computer.HasIndex(x => x.Serial).IsUnique();

        var vulnerability = modelBuilder.Entity<VulnerabilityRecord>();
        vulnerability.ToTable("vulnerabilities");
        vulnerability.HasKey(x => x.Id);
        vulnerability.Property(x => x.Cve).HasMaxLength(32).IsRequired();
        vulnerability.Property(x => x.Title).HasMaxLength(300).IsRequired();
        vulnerability.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        vulnerability.Property(x => x.AssetName).HasMaxLength(200).IsRequired();
        vulnerability.Property(x => x.Source).HasMaxLength(40).IsRequired();
        vulnerability.Property(x => x.IgnoreReason).HasMaxLength(500).HasDefaultValue("");
        vulnerability.HasIndex(x => new { x.Cve, x.AssetName }).IsUnique();

        var slaPolicy = modelBuilder.Entity<VulnerabilityPolicy>();
        slaPolicy.ToTable("vulnerability_policies");
        slaPolicy.HasKey(x => x.Id);

        var sla = modelBuilder.Entity<SlaEvent>();
        sla.ToTable("sla_events");
        sla.HasKey(x => x.Id);
        sla.Property(x => x.Cve).HasMaxLength(32).IsRequired();
        sla.Property(x => x.AssetName).HasMaxLength(200).IsRequired();
        sla.Property(x => x.EventKind).HasMaxLength(32).IsRequired();
        sla.Property(x => x.Detail).HasMaxLength(500).IsRequired();

        var trust = modelBuilder.Entity<TrustProfile>();
        trust.ToTable("trust_profiles");
        trust.HasKey(x => x.Id);
        trust.Property(x => x.Headline).HasMaxLength(200).IsRequired();
        trust.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
        trust.Property(x => x.ContactEmail).HasMaxLength(320).IsRequired();

        var trustDocument = modelBuilder.Entity<TrustDocument>();
        trustDocument.ToTable("trust_documents");
        trustDocument.HasKey(x => x.Id);
        trustDocument.Property(x => x.Title).HasMaxLength(200).IsRequired();
        trustDocument.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        trustDocument.Property(x => x.Access).HasMaxLength(16).IsRequired();
        trustDocument.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        trustDocument.Property(x => x.ObjectKey).HasMaxLength(200).HasDefaultValue("");
        trustDocument.Property(x => x.Sha256).HasMaxLength(64).HasDefaultValue("");
        trustDocument.Property(x => x.ContentType).HasMaxLength(100).HasDefaultValue("application/pdf");

        var trustRequest = modelBuilder.Entity<TrustAccessRequest>();
        trustRequest.ToTable("trust_requests");
        trustRequest.HasKey(x => x.Id);
        trustRequest.Property(x => x.RequesterName).HasMaxLength(200).IsRequired();
        trustRequest.Property(x => x.RequesterEmail).HasMaxLength(320).IsRequired();
        trustRequest.Property(x => x.Company).HasMaxLength(200).IsRequired();
        trustRequest.Property(x => x.Purpose).HasMaxLength(1000).IsRequired();
        trustRequest.Property(x => x.Status).HasMaxLength(16).IsRequired();
        trustRequest.Property(x => x.NdaAccepted).HasDefaultValue(false);
        trustRequest.Property(x => x.AccessToken).HasMaxLength(64).HasDefaultValue("");

        var rule = modelBuilder.Entity<AutomationRule>();
        rule.ToTable("automation_rules");
        rule.HasKey(x => x.Id);
        rule.Property(x => x.Name).HasMaxLength(200).IsRequired();
        rule.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        rule.Property(x => x.Action).HasMaxLength(16).IsRequired();
        rule.Property(x => x.Target).HasMaxLength(200).IsRequired();

        var delivery = modelBuilder.Entity<AutomationDelivery>();
        delivery.ToTable("automation_deliveries");
        delivery.HasKey(x => x.Id);
        delivery.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        delivery.Property(x => x.Action).HasMaxLength(16).IsRequired();
        delivery.Property(x => x.Target).HasMaxLength(200).IsRequired();
        delivery.Property(x => x.Summary).HasMaxLength(300).IsRequired();
        delivery.Property(x => x.ExternalId).HasMaxLength(80).IsRequired();

        var incident = modelBuilder.Entity<SecurityIncident>();
        incident.ToTable("incidents");
        incident.HasKey(x => x.Id);
        incident.Property(x => x.Code).HasMaxLength(16).IsRequired();
        incident.Property(x => x.Title).HasMaxLength(200).IsRequired();
        incident.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        incident.Property(x => x.Status).HasMaxLength(32).IsRequired();
        incident.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        incident.Property(x => x.ReporterName).HasMaxLength(200).IsRequired();
        incident.Property(x => x.ReporterEmail).HasMaxLength(320).IsRequired();
        incident.Property(x => x.RootCause).HasMaxLength(2000).HasDefaultValue("");
        incident.Property(x => x.ControlCode).HasMaxLength(32).HasDefaultValue("");
        incident.HasIndex(x => x.Code).IsUnique();

        var incidentEvent = modelBuilder.Entity<IncidentEvent>();
        incidentEvent.ToTable("incident_events");
        incidentEvent.HasKey(x => x.Id);
        incidentEvent.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        incidentEvent.Property(x => x.Detail).HasMaxLength(2000).IsRequired();
        incidentEvent.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        incidentEvent.HasOne(x => x.Incident).WithMany(x => x.Timeline).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);

        var change = modelBuilder.Entity<ChangeRecord>();
        change.ToTable("changes");
        change.HasKey(x => x.Id);
        change.Property(x => x.Code).HasMaxLength(16).IsRequired();
        change.Property(x => x.Title).HasMaxLength(200).IsRequired();
        change.Property(x => x.Source).HasMaxLength(16).IsRequired();
        change.Property(x => x.ExternalKey).HasMaxLength(80).HasDefaultValue("");
        change.Property(x => x.Repository).HasMaxLength(200).HasDefaultValue("");
        change.Property(x => x.AuthorName).HasMaxLength(200).HasDefaultValue("");
        change.Property(x => x.Status).HasMaxLength(16).IsRequired();
        change.Property(x => x.ApproverName).HasMaxLength(200).HasDefaultValue("");
        change.Property(x => x.DeployRef).HasMaxLength(80).HasDefaultValue("");
        change.HasIndex(x => x.Code).IsUnique();
        change.HasIndex(x => new { x.Source, x.ExternalKey });

        var questionnaire = modelBuilder.Entity<SecurityQuestionnaire>();
        questionnaire.ToTable("questionnaires");
        questionnaire.HasKey(x => x.Id);
        questionnaire.Property(x => x.Name).HasMaxLength(200).IsRequired();
        questionnaire.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        questionnaire.Property(x => x.Status).HasMaxLength(16).IsRequired();

        var answer = modelBuilder.Entity<QuestionnaireAnswer>();
        answer.ToTable("questionnaire_answers");
        answer.HasKey(x => x.Id);
        answer.Property(x => x.Question).HasMaxLength(1000).IsRequired();
        answer.Property(x => x.Suggestion).HasMaxLength(4000).HasDefaultValue("");
        answer.Property(x => x.Citation).HasMaxLength(300).HasDefaultValue("");
        answer.Property(x => x.Answer).HasMaxLength(4000).HasDefaultValue("");
        answer.Property(x => x.ReviewerName).HasMaxLength(200).HasDefaultValue("");
        answer.HasOne(x => x.Questionnaire).WithMany(x => x.Answers).HasForeignKey(x => x.QuestionnaireId).OnDelete(DeleteBehavior.Cascade);

        var description = modelBuilder.Entity<SystemDescription>();
        description.ToTable("system_descriptions");
        description.HasKey(x => x.Id);
        description.Property(x => x.Body).HasMaxLength(20000).IsRequired();
        description.Property(x => x.UpdatedBy).HasMaxLength(200).HasDefaultValue("");

        var impact = modelBuilder.Entity<ImpactAnalysis>();
        impact.ToTable("impact_analyses");
        impact.HasKey(x => x.Id);
        impact.Property(x => x.Name).HasMaxLength(200).IsRequired();
        impact.Property(x => x.Criticality).HasMaxLength(16).IsRequired();
        impact.Property(x => x.OwnerName).HasMaxLength(200).HasDefaultValue("");
        impact.Property(x => x.ControlCode).HasMaxLength(32).HasDefaultValue("");
        impact.Property(x => x.Notes).HasMaxLength(2000).HasDefaultValue("");

        var plan = modelBuilder.Entity<ContinuityPlan>();
        plan.ToTable("continuity_plans");
        plan.HasKey(x => x.Id);
        plan.Property(x => x.Name).HasMaxLength(200).IsRequired();
        plan.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        plan.Property(x => x.Body).HasMaxLength(8000).IsRequired();
        plan.Property(x => x.ControlCode).HasMaxLength(32).HasDefaultValue("");

        var exercise = modelBuilder.Entity<RecoveryExercise>();
        exercise.ToTable("recovery_exercises");
        exercise.HasKey(x => x.Id);
        exercise.Property(x => x.Name).HasMaxLength(200).IsRequired();
        exercise.Property(x => x.Result).HasMaxLength(16).IsRequired();
        exercise.Property(x => x.Notes).HasMaxLength(2000).HasDefaultValue("");
        exercise.Property(x => x.ControlCode).HasMaxLength(32).HasDefaultValue("");

        var pentest = modelBuilder.Entity<PentestReport>();
        pentest.ToTable("pentest_reports");
        pentest.HasKey(x => x.Id);
        pentest.Property(x => x.Title).HasMaxLength(200).IsRequired();
        pentest.Property(x => x.Vendor).HasMaxLength(200).IsRequired();
        pentest.Property(x => x.Summary).HasMaxLength(2000).HasDefaultValue("");

        var exposure = modelBuilder.Entity<ExposureFinding>();
        exposure.ToTable("exposure_findings");
        exposure.HasKey(x => x.Id);
        exposure.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        exposure.Property(x => x.ResourceName).HasMaxLength(200).IsRequired();
        exposure.Property(x => x.ResourceKind).HasMaxLength(32).IsRequired();
        exposure.Property(x => x.Region).HasMaxLength(32).IsRequired();
        exposure.Property(x => x.Cidr).HasMaxLength(64).IsRequired();
        exposure.Property(x => x.Protocol).HasMaxLength(16).IsRequired();
        exposure.Property(x => x.Service).HasMaxLength(64).IsRequired();
        exposure.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        exposure.Property(x => x.ControlCode).HasMaxLength(32).IsRequired();
        exposure.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        exposure.Property(x => x.RemediationTerraform).HasMaxLength(2000).IsRequired();
        exposure.Property(x => x.RemediationCli).HasMaxLength(1000).IsRequired();
        exposure.Property(x => x.Status).HasMaxLength(16).IsRequired();
        exposure.Property(x => x.ResolutionNote).HasMaxLength(500).IsRequired();
        exposure.HasIndex(x => new { x.Provider, x.ResourceName, x.Port });

        var scan = modelBuilder.Entity<SecurityScanFinding>();
        scan.ToTable("security_scan_findings");
        scan.HasKey(x => x.Id);
        scan.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        scan.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        scan.Property(x => x.AssetName).HasMaxLength(200).IsRequired();
        scan.Property(x => x.Location).HasMaxLength(300).IsRequired();
        scan.Property(x => x.RuleId).HasMaxLength(80).IsRequired();
        scan.Property(x => x.Title).HasMaxLength(300).IsRequired();
        scan.Property(x => x.Detail).HasMaxLength(1000).IsRequired();
        scan.Property(x => x.Snippet).HasMaxLength(500).IsRequired();
        scan.Property(x => x.Source).HasMaxLength(40).IsRequired();
        scan.Property(x => x.FixVersion).HasMaxLength(80).IsRequired();
        scan.Property(x => x.ControlCode).HasMaxLength(32).IsRequired();
        scan.Property(x => x.Status).HasMaxLength(16).IsRequired();
        scan.HasIndex(x => new { x.Kind, x.AssetName, x.RuleId });

        var dataStore = modelBuilder.Entity<DataStoreAsset>();
        dataStore.ToTable("data_store_assets");
        dataStore.HasKey(x => x.Id);
        dataStore.Property(x => x.StoreName).HasMaxLength(200).IsRequired();
        dataStore.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        dataStore.Property(x => x.Engine).HasMaxLength(40).IsRequired();
        dataStore.Property(x => x.Region).HasMaxLength(32).IsRequired();
        dataStore.Property(x => x.Classification).HasMaxLength(32).IsRequired();
        dataStore.Property(x => x.SensitiveDataTypes).HasMaxLength(300).IsRequired();
        dataStore.Property(x => x.ControlCode).HasMaxLength(32).IsRequired();
        dataStore.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        dataStore.HasIndex(x => new { x.Provider, x.StoreName }).IsUnique();

        var cost = modelBuilder.Entity<CostRecommendation>();
        cost.ToTable("cost_recommendations");
        cost.HasKey(x => x.Id);
        cost.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        cost.Property(x => x.ResourceName).HasMaxLength(200).IsRequired();
        cost.Property(x => x.ResourceType).HasMaxLength(64).IsRequired();
        cost.Property(x => x.Region).HasMaxLength(32).IsRequired();
        cost.Property(x => x.Category).HasMaxLength(32).IsRequired();
        cost.Property(x => x.MonthlySavingsUsd).HasPrecision(12, 2);
        cost.Property(x => x.AnnualSavingsUsd).HasPrecision(12, 2);
        cost.Property(x => x.LinkedControlCode).HasMaxLength(32).IsRequired();
        cost.Property(x => x.ComplianceImpact).HasMaxLength(500).IsRequired();
        cost.Property(x => x.RemediationSummary).HasMaxLength(500).IsRequired();
        cost.Property(x => x.Status).HasMaxLength(16).IsRequired();
        cost.HasIndex(x => new { x.Provider, x.ResourceName, x.Category });

        var apiKey = modelBuilder.Entity<ApiKeyRecord>();
        apiKey.ToTable("api_keys");
        apiKey.HasKey(x => x.Id);
        apiKey.Property(x => x.Name).HasMaxLength(200).IsRequired();
        apiKey.Property(x => x.Prefix).HasMaxLength(32).IsRequired();
        apiKey.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
        apiKey.Property(x => x.Scopes).HasMaxLength(500).IsRequired();
        apiKey.Property(x => x.CreatedByEmail).HasMaxLength(320).IsRequired();
        apiKey.HasIndex(x => x.KeyHash).IsUnique();

        var webhook = modelBuilder.Entity<WebhookSubscription>();
        webhook.ToTable("webhooks");
        webhook.HasKey(x => x.Id);
        webhook.Property(x => x.Name).HasMaxLength(200).IsRequired();
        webhook.Property(x => x.Url).HasMaxLength(500).IsRequired();
        webhook.Property(x => x.Events).HasMaxLength(500).IsRequired();
        webhook.Property(x => x.SigningSecretHash).HasMaxLength(64).IsRequired();
        webhook.Property(x => x.SigningSecretEncrypted).HasMaxLength(2000).HasDefaultValue("").IsRequired();
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
