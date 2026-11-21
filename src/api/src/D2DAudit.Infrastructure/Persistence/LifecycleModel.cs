using D2DAudit.Domain;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Persistence;

/// <summary>Tenant tables for personnel lifecycle and vendor risk management.</summary>
public static class LifecycleModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var group = modelBuilder.Entity<EmployeeGroup>();
        group.ToTable("employee_groups");
        group.HasKey(x => x.Id);
        group.Property(x => x.Code).HasMaxLength(64).IsRequired();
        group.Property(x => x.Name).HasMaxLength(120).IsRequired();
        group.Property(x => x.Description).HasMaxLength(500).IsRequired();
        group.Property(x => x.Departments).HasMaxLength(1000).IsRequired();
        group.Property(x => x.RoleKeywords).HasMaxLength(1000).IsRequired();
        group.Property(x => x.PolicyCodes).HasMaxLength(1000).IsRequired();
        group.Property(x => x.TrainingCodes).HasMaxLength(1000).IsRequired();
        group.HasIndex(x => x.Code).IsUnique();

        var template = modelBuilder.Entity<LifecycleTemplateStep>();
        template.ToTable("lifecycle_template_steps");
        template.HasKey(x => x.Id);
        template.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        template.Property(x => x.Code).HasMaxLength(64).IsRequired();
        template.Property(x => x.Title).HasMaxLength(200).IsRequired();
        template.Property(x => x.Category).HasMaxLength(64).IsRequired();
        template.Property(x => x.GroupCode).HasMaxLength(64).IsRequired();
        template.HasIndex(x => new { x.Kind, x.Code, x.GroupCode }).IsUnique();

        var settings = modelBuilder.Entity<LifecycleSettings>();
        settings.ToTable("lifecycle_settings");
        settings.HasKey(x => x.Id);

        var checklist = modelBuilder.Entity<LifecycleChecklist>();
        checklist.ToTable("lifecycle_checklists");
        checklist.HasKey(x => x.Id);
        checklist.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        checklist.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        checklist.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        checklist.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        checklist.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        checklist.Property(x => x.State).HasMaxLength(32).IsRequired();
        checklist.Property(x => x.StartedBy).HasMaxLength(200).IsRequired();
        checklist.HasIndex(x => new { x.PersonId, x.Kind });
        checklist.HasMany(x => x.Steps).WithOne(x => x.Checklist).HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.Cascade);

        var step = modelBuilder.Entity<LifecycleStep>();
        step.ToTable("lifecycle_steps");
        step.HasKey(x => x.Id);
        step.Property(x => x.Code).HasMaxLength(64).IsRequired();
        step.Property(x => x.Title).HasMaxLength(300).IsRequired();
        step.Property(x => x.Category).HasMaxLength(64).IsRequired();
        step.Property(x => x.Provider).HasMaxLength(100).IsRequired();
        step.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
        step.Property(x => x.CompletedBy).HasMaxLength(200).IsRequired();
        step.Property(x => x.Result).HasMaxLength(1000).IsRequired();

        var profile = modelBuilder.Entity<VendorProfile>();
        profile.ToTable("vendor_profiles");
        profile.HasKey(x => x.Id);
        profile.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        profile.Property(x => x.OwnerEmail).HasMaxLength(320).IsRequired();
        profile.Property(x => x.DataCategories).HasMaxLength(500).IsRequired();
        profile.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
        profile.Property(x => x.ContactEmail).HasMaxLength(320).IsRequired();
        profile.HasIndex(x => x.VendorId).IsUnique();
        profile.HasOne<VendorRecord>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);

        var suggestion = modelBuilder.Entity<VendorDiscoverySuggestion>();
        suggestion.ToTable("vendor_discovery_suggestions");
        suggestion.HasKey(x => x.Id);
        suggestion.Property(x => x.Name).HasMaxLength(200).IsRequired();
        suggestion.Property(x => x.Source).HasMaxLength(64).IsRequired();
        suggestion.Property(x => x.Detail).HasMaxLength(500).IsRequired();
        suggestion.Property(x => x.SuggestedCriticality).HasMaxLength(32).IsRequired();
        suggestion.Property(x => x.DataCategories).HasMaxLength(500).IsRequired();
        suggestion.Property(x => x.Status).HasMaxLength(32).IsRequired();
        suggestion.Property(x => x.DecidedBy).HasMaxLength(200).IsRequired();
        suggestion.HasIndex(x => x.Name).IsUnique();

        var assessment = modelBuilder.Entity<VendorAssessment>();
        assessment.ToTable("vendor_assessments");
        assessment.HasKey(x => x.Id);
        assessment.Property(x => x.VendorName).HasMaxLength(200).IsRequired();
        assessment.Property(x => x.TemplateCode).HasMaxLength(64).IsRequired();
        assessment.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
        assessment.Property(x => x.ContactEmail).HasMaxLength(320).IsRequired();
        assessment.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        assessment.Property(x => x.Status).HasMaxLength(32).IsRequired();
        assessment.Property(x => x.SentBy).HasMaxLength(200).IsRequired();
        assessment.Property(x => x.DeliveryReceipt).HasMaxLength(200).IsRequired();
        assessment.Property(x => x.ReviewedBy).HasMaxLength(200).IsRequired();
        assessment.Property(x => x.ReviewNote).HasMaxLength(1000).IsRequired();
        assessment.Property(x => x.Rating).HasMaxLength(32).IsRequired();
        assessment.HasIndex(x => x.TokenHash).IsUnique();
        assessment.HasIndex(x => x.VendorId);
        assessment.HasOne<VendorRecord>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        assessment.HasMany(x => x.Answers).WithOne(x => x.Assessment).HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);

        var answer = modelBuilder.Entity<VendorAssessmentAnswer>();
        answer.ToTable("vendor_assessment_answers");
        answer.HasKey(x => x.Id);
        answer.Property(x => x.QuestionCode).HasMaxLength(32).IsRequired();
        answer.Property(x => x.Answer).HasMaxLength(32).IsRequired();
        answer.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
        answer.HasIndex(x => new { x.AssessmentId, x.QuestionCode }).IsUnique();

        var document = modelBuilder.Entity<VendorReviewDocument>();
        document.ToTable("vendor_review_documents");
        document.HasKey(x => x.Id);
        document.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        document.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        document.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        document.Property(x => x.ObjectKey).HasMaxLength(300).IsRequired();
        document.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        document.Property(x => x.UploadedBy).HasMaxLength(200).IsRequired();
        document.Property(x => x.ReportPeriod).HasMaxLength(200).IsRequired();
        document.Property(x => x.Opinion).HasMaxLength(64).IsRequired();
        document.Property(x => x.Exceptions).HasMaxLength(4000).IsRequired();
        document.Property(x => x.Cuecs).HasMaxLength(4000).IsRequired();
        document.Property(x => x.SummaryText).HasMaxLength(8000).IsRequired();
        document.Property(x => x.SummaryMode).HasMaxLength(32).IsRequired();
        document.Property(x => x.SignedOffBy).HasMaxLength(200).IsRequired();
        document.Property(x => x.SignOffNote).HasMaxLength(1000).IsRequired();
        document.HasIndex(x => x.VendorId);
        document.HasOne<VendorRecord>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
    }
}
