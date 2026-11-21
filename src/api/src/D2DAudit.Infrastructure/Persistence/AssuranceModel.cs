using D2DAudit.Domain;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Persistence;

/// <summary>Tables for custom tests, the evidence library, control insights, and policy attestations.</summary>
internal static class AssuranceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var definition = modelBuilder.Entity<CustomTestDefinition>();
        definition.ToTable("custom_test_definitions");
        definition.HasKey(x => x.Id);
        definition.Property(x => x.Key).HasMaxLength(64).IsRequired();
        definition.Property(x => x.Name).HasMaxLength(200).IsRequired();
        definition.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        definition.Property(x => x.ResourceKind).HasMaxLength(64).IsRequired();
        definition.Property(x => x.Provider).HasMaxLength(64);
        definition.Property(x => x.FilterJson).HasMaxLength(ConditionEvaluator.MaxJsonLength);
        definition.Property(x => x.AssertionJson).HasMaxLength(ConditionEvaluator.MaxJsonLength).IsRequired();
        definition.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        definition.Property(x => x.ControlCodes).HasMaxLength(400).IsRequired();
        definition.Property(x => x.Remediation).HasMaxLength(2000).IsRequired();
        definition.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
        definition.HasIndex(x => x.Key).IsUnique();

        var suppression = modelBuilder.Entity<TestSuppression>();
        suppression.ToTable("test_suppressions");
        suppression.HasKey(x => x.Id);
        suppression.Property(x => x.TestKey).HasMaxLength(64).IsRequired();
        suppression.Property(x => x.State).HasMaxLength(16).IsRequired();
        suppression.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        suppression.Property(x => x.ByName).HasMaxLength(200).IsRequired();
        suppression.HasIndex(x => x.TestKey).IsUnique();

        var document = modelBuilder.Entity<EvidenceDocument>();
        document.ToTable("evidence_documents");
        document.HasKey(x => x.Id);
        document.Property(x => x.Code).HasMaxLength(32).IsRequired();
        document.Property(x => x.Title).HasMaxLength(200).IsRequired();
        document.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        document.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        document.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        document.Property(x => x.Cadence).HasConversion<string>().HasMaxLength(16);
        document.Property(x => x.CurrentSha256).HasMaxLength(64);
        document.HasIndex(x => x.Code).IsUnique();
        document.HasMany(x => x.Links).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        document.HasMany(x => x.Requests).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);

        var link = modelBuilder.Entity<EvidenceDocumentLink>();
        link.ToTable("evidence_document_links");
        link.HasKey(x => new { x.DocumentId, x.ControlCode });
        link.Property(x => x.ControlCode).HasMaxLength(32).IsRequired();
        link.HasIndex(x => x.ControlCode);

        var request = modelBuilder.Entity<EvidenceRequest>();
        request.ToTable("evidence_requests");
        request.HasKey(x => x.Id);
        request.Property(x => x.AssigneeName).HasMaxLength(200).IsRequired();
        request.Property(x => x.AssigneeEmail).HasMaxLength(320).IsRequired();
        request.Property(x => x.Note).HasMaxLength(1000).IsRequired();
        request.Property(x => x.Status).HasMaxLength(16).IsRequired();
        request.Property(x => x.RequestedByName).HasMaxLength(200).IsRequired();
        request.HasIndex(x => x.AssigneeEmail);

        var profile = modelBuilder.Entity<ControlProfile>();
        profile.ToTable("control_profiles");
        profile.HasKey(x => x.ControlCode);
        profile.Property(x => x.ControlCode).HasMaxLength(32);
        profile.Property(x => x.ImplementationGuidance).HasMaxLength(4000).IsRequired();
        profile.Property(x => x.EvidenceRequirement).HasMaxLength(2000).IsRequired();

        var comment = modelBuilder.Entity<ControlComment>();
        comment.ToTable("control_comments");
        comment.HasKey(x => x.Id);
        comment.Property(x => x.ControlCode).HasMaxLength(32).IsRequired();
        comment.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        comment.Property(x => x.AuthorEmail).HasMaxLength(320).IsRequired();
        comment.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        comment.HasIndex(x => x.ControlCode);
        comment.HasMany(x => x.Revisions).WithOne(x => x.Comment).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);

        var revision = modelBuilder.Entity<ControlCommentRevision>();
        revision.ToTable("control_comment_revisions");
        revision.HasKey(x => x.Id);
        revision.Property(x => x.Body).HasMaxLength(2000).IsRequired();

        var campaign = modelBuilder.Entity<PolicyAttestationCampaign>();
        campaign.ToTable("policy_attestation_campaigns");
        campaign.HasKey(x => x.Id);
        campaign.Property(x => x.PolicyCode).HasMaxLength(32).IsRequired();
        campaign.Property(x => x.Group).HasMaxLength(100).IsRequired();
        campaign.Property(x => x.State).HasMaxLength(16).IsRequired();
        campaign.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
        campaign.HasIndex(x => x.PolicyCode);
        campaign.HasMany(x => x.Attestations).WithOne(x => x.Campaign).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);

        var attestation = modelBuilder.Entity<PolicyAttestation>();
        attestation.ToTable("policy_attestations");
        attestation.HasKey(x => x.Id);
        attestation.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        attestation.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        attestation.HasIndex(x => new { x.CampaignId, x.PersonEmail }).IsUnique();
    }
}
