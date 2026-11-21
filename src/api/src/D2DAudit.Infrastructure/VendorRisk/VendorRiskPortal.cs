using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.VendorRisk;

/// <summary>Shared vendor risk helpers used by the workspace portal and the public vendor portal.</summary>
public static class VendorRiskShared
{
    public const int MaxDocumentBytes = 700 * 1024;

    public const int MaxDocumentsPerAssessment = 10;

    public static readonly IReadOnlyList<string> DocumentKinds = ["Soc2", "Iso27001", "PenTest", "Other"];

    public static readonly IReadOnlyList<string> RequirementCodes = ["CC9.2", "A.5.19", "A.5.20", "A.5.21", "A.5.22"];

    public static string EffectiveStatus(VendorAssessment item, DateTimeOffset now) =>
        item.Status is "Sent" or "InProgress" && now >= item.ExpiresAt ? "Expired" : item.Status;

    public static bool Editable(string status) => status is "Sent" or "InProgress";

    public static string CleanFileName(string? name)
    {
        var raw = Path.GetFileName((name ?? "").Trim());
        var clean = new string(raw.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ' ').ToArray()).Trim();
        if (clean.Length > 120)
        {
            clean = clean[^120..];
        }

        return clean;
    }

    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    public static byte[]? Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > (MaxDocumentBytes / 3 * 4) + 8)
        {
            return null;
        }

        var buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64.Trim(), buffer, out var written) && written is > 0 and <= MaxDocumentBytes
            ? buffer[..written]
            : null;
    }

    public static void ApplySummary(VendorReviewDocument document, Soc2Summary summary)
    {
        document.ReportPeriod = Trim(summary.ReportPeriod, 200);
        document.Opinion = Trim(summary.Opinion, 64);
        document.Exceptions = Trim(string.Join('\n', summary.Exceptions), 4000);
        document.Cuecs = Trim(string.Join('\n', summary.Cuecs), 4000);
    }

    public static string DeterministicText(string vendor, Soc2Summary summary)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInvariant($"{summary.ReportType} report for {vendor}. "));
        builder.Append(CultureInvariant($"Period: {summary.ReportPeriod}. Opinion: {summary.Opinion}. "));
        builder.Append(summary.Exceptions.Count == 0
            ? "No exceptions were found in the text. "
            : CultureInvariant($"{summary.Exceptions.Count} exception(s) noted; review each before sign-off. "));
        builder.Append(summary.Cuecs.Count == 0
            ? "No complementary user entity controls were listed."
            : CultureInvariant($"{summary.Cuecs.Count} complementary user entity control(s) must be operated by your company."));
        return builder.ToString();
    }

    public static IReadOnlyList<string> Lines(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string CultureInvariant(FormattableString value) => FormattableString.Invariant(value);
}

public sealed class VendorRiskPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IStorageProvider storage,
    IActivityWriter activity,
    INotifier notifier,
    IAssistantProvider assistant,
    IVendorDiscoverySource discovery,
    IConfiguration configuration,
    TimeProvider time) : IVendorRisk
{
    private const string ReviewTaskPrefix = "Vendor security review: ";
    private const string RenewalTaskPrefix = "Vendor contract renewal: ";

    public async Task<IReadOnlyList<VendorRiskRow>> InventoryAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = OpenDb();
        await EnsureExampleAsync(db, cancellationToken);
        var vendors = await db.Vendors.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var profiles = await db.VendorProfiles.AsNoTracking().ToDictionaryAsync(item => item.VendorId, cancellationToken);
        var assessments = await LatestAssessmentsAsync(db, cancellationToken);
        return vendors.Select(vendor => ToRow(vendor, profiles.GetValueOrDefault(vendor.Id), assessments.GetValueOrDefault(vendor.Id))).ToArray();
    }

    public async Task<Result<VendorRiskDetailDto>> DetailAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<VendorRiskDetailDto>.Fail("forbidden");
        }

        await using var db = OpenDb();
        await EnsureExampleAsync(db, cancellationToken);
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(item => item.Id == vendorId, cancellationToken);
        if (vendor is null)
        {
            return Result<VendorRiskDetailDto>.Fail("not_found");
        }

        var profile = await db.VendorProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.VendorId == vendorId, cancellationToken);
        var assessments = await db.VendorAssessments.AsNoTracking().Include(item => item.Answers)
            .Where(item => item.VendorId == vendorId)
            .OrderByDescending(item => item.SentAt)
            .ToListAsync(cancellationToken);
        var documents = await db.VendorReviewDocuments.AsNoTracking()
            .Where(item => item.VendorId == vendorId)
            .OrderByDescending(item => item.UploadedAt)
            .ToListAsync(cancellationToken);
        var reminders = await db.WorkTasks.AsNoTracking()
            .Where(item => item.LinkedKind == "Vendor" && item.LinkedCode == vendor.Name)
            .OrderBy(item => item.DueOn)
            .ToListAsync(cancellationToken);
        return Result<VendorRiskDetailDto>.Ok(new VendorRiskDetailDto(
            ToRow(vendor, profile, assessments.FirstOrDefault()),
            assessments.Select(ToAssessment).ToArray(),
            documents.Select(item => ToDocument(item, vendor.Name)).ToArray(),
            reminders.Select(ToReminder).ToArray()));
    }

    public async Task<Result<VendorRiskRow>> SaveProfileAsync(Guid vendorId, SaveVendorProfileRequest request, CancellationToken cancellationToken)
    {
        var owner = (request.OwnerName ?? "").Trim();
        var ownerEmail = (request.OwnerEmail ?? "").Trim();
        var contact = (request.ContactName ?? "").Trim();
        var contactEmail = (request.ContactEmail ?? "").Trim();
        var categories = (request.DataCategories ?? []).Select(item => (item ?? "").Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (owner.Length > 200 || contact.Length > 200
            || !OptionalEmail(ownerEmail) || !OptionalEmail(contactEmail)
            || categories.Length > 12 || categories.Any(item => item.Length > 40 || item.Contains(','))
            || request.ReviewCadenceMonths is < 1 or > 60)
        {
            return Result<VendorRiskRow>.Fail("invalid_vendor_profile");
        }

        await using var db = OpenDb();
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(item => item.Id == vendorId, cancellationToken);
        if (vendor is null)
        {
            return Result<VendorRiskRow>.Fail("not_found");
        }

        var profile = await db.VendorProfiles.FirstOrDefaultAsync(item => item.VendorId == vendorId, cancellationToken);
        if (profile is null)
        {
            profile = new VendorProfile { Id = Guid.NewGuid(), VendorId = vendorId };
            db.VendorProfiles.Add(profile);
        }

        var risk = VendorRiskScoring.Inherent(vendor.Criticality, categories, request.Subprocessor);
        profile.OwnerName = owner;
        profile.OwnerEmail = ownerEmail;
        profile.ContractRenewalOn = request.ContractRenewalOn;
        profile.DataCategories = string.Join(',', categories);
        profile.Subprocessor = request.Subprocessor;
        profile.ReviewCadenceMonths = request.ReviewCadenceMonths ?? risk.DefaultCadenceMonths;
        profile.LastReviewedOn = request.LastReviewedOn;
        profile.ContactName = contact;
        profile.ContactEmail = contactEmail;
        profile.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.profile", vendor.Name, cancellationToken);
        var latest = (await LatestAssessmentsAsync(db, cancellationToken)).GetValueOrDefault(vendorId);
        return Result<VendorRiskRow>.Ok(ToRow(vendor, profile, latest));
    }

    public async Task<IReadOnlyList<VendorDiscoveryDto>> DiscoveryAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = OpenDb();
        await EnsureExampleAsync(db, cancellationToken);
        var rows = await db.VendorDiscoverySuggestions.AsNoTracking().OrderBy(item => item.Status == "New" ? 0 : 1).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        return rows.Select(ToDiscovery).ToArray();
    }

    public async Task<Result<VendorDiscoveryScanResult>> ScanAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var found = await discovery.DiscoverAsync(cancellationToken);
        var added = await AddSuggestionsAsync(db, found, cancellationToken);
        await WriteAsync("vendor.discovery.scan", $"{found.Count} found, {added} new ({discovery.Name})", cancellationToken);
        var rows = await db.VendorDiscoverySuggestions.AsNoTracking().OrderBy(item => item.Status == "New" ? 0 : 1).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        return Result<VendorDiscoveryScanResult>.Ok(new VendorDiscoveryScanResult(
            found.Count,
            added,
            found.Select(item => item.Source).Distinct(StringComparer.Ordinal).ToArray(),
            rows.Select(ToDiscovery).ToArray()));
    }

    public async Task<Result<VendorDiscoveryDto>> DecideAsync(Guid id, bool accept, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var suggestion = await db.VendorDiscoverySuggestions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (suggestion is null)
        {
            return Result<VendorDiscoveryDto>.Fail("not_found");
        }

        if (suggestion.Status != "New")
        {
            return Result<VendorDiscoveryDto>.Fail("already_decided");
        }

        var now = time.GetUtcNow();
        suggestion.Status = accept ? "Accepted" : "Ignored";
        suggestion.DecidedAt = now;
        suggestion.DecidedBy = Actor();
        if (accept)
        {
            var name = suggestion.Name;
            var vendor = await db.Vendors.FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);
            if (vendor is null)
            {
                var criticality = Enum.TryParse<VendorCriticality>(suggestion.SuggestedCriticality, out var parsed) ? parsed : VendorCriticality.Medium;
                vendor = new VendorRecord
                {
                    Id = Guid.NewGuid(),
                    Name = suggestion.Name,
                    Service = $"Discovered from {suggestion.Source}",
                    Criticality = criticality,
                    DataAccess = suggestion.DataCategories.Length > 0 ? suggestion.DataCategories.Replace(",", ", ", StringComparison.Ordinal) : "To be assessed",
                    ReviewState = VendorReviewState.NotStarted,
                    ReviewDueOn = Today().AddDays(30),
                    RiskRating = 0,
                    Decision = "Pending",
                };
                db.Vendors.Add(vendor);
                var categories = LifecycleRules.Split(suggestion.DataCategories);
                db.VendorProfiles.Add(new VendorProfile
                {
                    Id = Guid.NewGuid(),
                    VendorId = vendor.Id,
                    OwnerName = Actor(),
                    OwnerEmail = tenant.Email,
                    DataCategories = suggestion.DataCategories,
                    ReviewCadenceMonths = VendorRiskScoring.Inherent(criticality, categories, false).DefaultCadenceMonths,
                    UpdatedAt = now,
                });
            }

            suggestion.VendorId = vendor.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync(accept ? "vendor.discovery.accept" : "vendor.discovery.ignore", suggestion.Name, cancellationToken);
        return Result<VendorDiscoveryDto>.Ok(ToDiscovery(suggestion));
    }

    public IReadOnlyList<QuestionnaireTemplateDto> Templates() =>
        VendorQuestionnaireLibrary.Templates.Select(ToTemplate).ToArray();

    public async Task<IReadOnlyList<VendorAssessmentDto>> AssessmentsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = OpenDb();
        await EnsureExampleAsync(db, cancellationToken);
        var rows = await db.VendorAssessments.AsNoTracking().Include(item => item.Answers).OrderByDescending(item => item.SentAt).Take(200).ToListAsync(cancellationToken);
        return rows.Select(ToAssessment).ToArray();
    }

    public async Task<Result<SentVendorQuestionnaire>> SendAsync(Guid vendorId, SendVendorQuestionnaireRequest request, CancellationToken cancellationToken)
    {
        var template = VendorQuestionnaireLibrary.Find(request.TemplateCode);
        var contact = (request.ContactName ?? "").Trim();
        var email = (request.ContactEmail ?? "").Trim();
        var days = request.ExpiresInDays ?? 14;
        if (template is null || contact.Length is 0 or > 200 || email.Length == 0 || !OptionalEmail(email) || days is < 1 or > 60)
        {
            return Result<SentVendorQuestionnaire>.Fail("invalid_questionnaire");
        }

        await using var db = OpenDb();
        var vendor = await db.Vendors.FirstOrDefaultAsync(item => item.Id == vendorId, cancellationToken);
        if (vendor is null)
        {
            return Result<SentVendorQuestionnaire>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        var (token, hash) = VendorPortalTokens.Create(tenant.TenantId);
        var assessment = new VendorAssessment
        {
            Id = Guid.NewGuid(),
            VendorId = vendor.Id,
            VendorName = vendor.Name,
            TemplateCode = template.Code,
            ContactName = contact,
            ContactEmail = email,
            TokenHash = hash,
            ExpiresAt = now.AddDays(days),
            Status = "Sent",
            SentAt = now,
            SentBy = Actor(),
        };
        var path = $"/vendor-portal/{token}";
        var link = PortalLink(path);
        var detail = $"Hello {contact},\n\n{tenant.TenantName} asks {vendor.Name} to complete the \"{template.Name}\" security questionnaire ({template.Questions.Count} questions) and to upload a current SOC 2 report or ISO 27001 certificate.\n\nOpen the private link below. It expires on {assessment.ExpiresAt:yyyy-MM-dd} and works only for this request:\n{link}\n\nNo account is needed.";
        assessment.DeliveryReceipt = VendorRiskShared.Trim(await notifier.SendAsync("email", email, $"Security questionnaire from {tenant.TenantName}", detail, cancellationToken), 200);
        db.VendorAssessments.Add(assessment);
        if (vendor.ReviewState is VendorReviewState.NotStarted or VendorReviewState.Expired)
        {
            vendor.ReviewState = VendorReviewState.InReview;
        }

        var profile = await db.VendorProfiles.FirstOrDefaultAsync(item => item.VendorId == vendor.Id, cancellationToken);
        if (profile is not null && profile.ContactEmail.Length == 0)
        {
            profile.ContactName = contact;
            profile.ContactEmail = email;
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.questionnaire.send", $"{vendor.Name} {template.Code} {assessment.DeliveryReceipt}", cancellationToken);
        return Result<SentVendorQuestionnaire>.Ok(new SentVendorQuestionnaire(ToAssessment(assessment), path));
    }

    public async Task<Result<VendorAssessmentDto>> ReviewAsync(Guid assessmentId, ReviewVendorAssessmentRequest request, CancellationToken cancellationToken)
    {
        var note = (request.Note ?? "").Trim();
        if (request.Decision is not ("Approve" or "Reject" or "Reopen") || note.Length > 1000)
        {
            return Result<VendorAssessmentDto>.Fail("invalid_review");
        }

        await using var db = OpenDb();
        var assessment = await db.VendorAssessments.Include(item => item.Answers).FirstOrDefaultAsync(item => item.Id == assessmentId, cancellationToken);
        if (assessment is null)
        {
            return Result<VendorAssessmentDto>.Fail("not_found");
        }

        if (assessment.Status != "Submitted")
        {
            return Result<VendorAssessmentDto>.Fail("not_submitted");
        }

        var now = time.GetUtcNow();
        var vendor = await db.Vendors.FirstAsync(item => item.Id == assessment.VendorId, cancellationToken);
        if (request.Decision == "Reopen")
        {
            assessment.Status = "InProgress";
            assessment.SubmittedAt = null;
            if (assessment.ExpiresAt <= now)
            {
                assessment.ExpiresAt = now.AddDays(7);
            }
        }
        else
        {
            assessment.Status = "Reviewed";
            assessment.ReviewedAt = now;
            assessment.ReviewedBy = Actor();
            vendor.Decision = request.Decision == "Approve" ? "Approved" : "Rejected";
            vendor.ReviewState = request.Decision == "Approve" ? VendorReviewState.Approved : VendorReviewState.InReview;
            var profile = await db.VendorProfiles.FirstOrDefaultAsync(item => item.VendorId == vendor.Id, cancellationToken);
            if (profile is null)
            {
                profile = new VendorProfile { Id = Guid.NewGuid(), VendorId = vendor.Id, DataCategories = string.Join(',', VendorRiskScoring.InferCategories(vendor.DataAccess)) };
                profile.ReviewCadenceMonths = VendorRiskScoring.Inherent(vendor.Criticality, LifecycleRules.Split(profile.DataCategories), false).DefaultCadenceMonths;
                db.VendorProfiles.Add(profile);
            }

            if (request.Decision == "Approve")
            {
                profile.LastReviewedOn = Today();
                vendor.ReviewDueOn = Today().AddMonths(profile.ReviewCadenceMonths);
            }

            profile.UpdatedAt = now;
        }

        assessment.ReviewNote = note;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.questionnaire.review", $"{assessment.VendorName} {request.Decision}", cancellationToken);
        return Result<VendorAssessmentDto>.Ok(ToAssessment(assessment));
    }

    public async Task<Result<VendorAssessmentDto>> RevokeAsync(Guid assessmentId, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var assessment = await db.VendorAssessments.Include(item => item.Answers).FirstOrDefaultAsync(item => item.Id == assessmentId, cancellationToken);
        if (assessment is null)
        {
            return Result<VendorAssessmentDto>.Fail("not_found");
        }

        if (assessment.Status is "Reviewed" or "Revoked")
        {
            return Result<VendorAssessmentDto>.Fail("not_revocable");
        }

        assessment.Status = "Revoked";
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.questionnaire.revoke", assessment.VendorName, cancellationToken);
        return Result<VendorAssessmentDto>.Ok(ToAssessment(assessment));
    }

    public async Task<IReadOnlyList<VendorReviewDocumentDto>> DocumentsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = OpenDb();
        await EnsureExampleAsync(db, cancellationToken);
        var names = await db.Vendors.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var rows = await db.VendorReviewDocuments.AsNoTracking().OrderByDescending(item => item.UploadedAt).Take(200).ToListAsync(cancellationToken);
        return rows.Select(item => ToDocument(item, names.GetValueOrDefault(item.VendorId, ""))).ToArray();
    }

    public async Task<Result<VendorReviewDocumentDto>> UploadAsync(Guid vendorId, UploadVendorDocumentRequest request, CancellationToken cancellationToken)
    {
        var fileName = VendorRiskShared.CleanFileName(request.FileName);
        var bytes = VendorRiskShared.Decode(request.ContentBase64);
        if (!VendorRiskShared.DocumentKinds.Contains(request.Kind ?? "", StringComparer.Ordinal) || fileName.Length == 0 || bytes is null)
        {
            return Result<VendorReviewDocumentDto>.Fail("invalid_document");
        }

        await using var db = OpenDb();
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(item => item.Id == vendorId, cancellationToken);
        if (vendor is null)
        {
            return Result<VendorReviewDocumentDto>.Fail("not_found");
        }

        var document = await StoreAsync(db, tenant.TenantId, vendor.Id, null, request.Kind!, fileName, bytes, Actor(), cancellationToken);
        if (document.Kind == "Soc2")
        {
            await SummarizeCoreAsync(document, vendor.Name, bytes, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.document.upload", $"{vendor.Name} {document.Kind} {document.FileName}", cancellationToken);
        return Result<VendorReviewDocumentDto>.Ok(ToDocument(document, vendor.Name));
    }

    public async Task<Result<VendorReviewDocumentDto>> SummarizeAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var document = await db.VendorReviewDocuments.FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Result<VendorReviewDocumentDto>.Fail("not_found");
        }

        var vendor = await db.Vendors.AsNoTracking().FirstAsync(item => item.Id == document.VendorId, cancellationToken);
        byte[] bytes;
        try
        {
            bytes = await storage.GetAsync(document.ObjectKey, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            return Result<VendorReviewDocumentDto>.Fail("document_unavailable");
        }

        await SummarizeCoreAsync(document, vendor.Name, bytes, cancellationToken);
        document.SignedOffAt = null;
        document.SignedOffBy = "";
        document.SignOffNote = "";
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.document.summarize", $"{vendor.Name} {document.FileName} {document.SummaryMode}", cancellationToken);
        return Result<VendorReviewDocumentDto>.Ok(ToDocument(document, vendor.Name));
    }

    public async Task<Result<VendorReviewDocumentDto>> SignOffAsync(Guid documentId, SignOffVendorDocumentRequest request, CancellationToken cancellationToken)
    {
        var note = (request.Note ?? "").Trim();
        if (note.Length > 1000)
        {
            return Result<VendorReviewDocumentDto>.Fail("invalid_signoff");
        }

        await using var db = OpenDb();
        var document = await db.VendorReviewDocuments.FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Result<VendorReviewDocumentDto>.Fail("not_found");
        }

        if (document.SummarizedAt is null)
        {
            return Result<VendorReviewDocumentDto>.Fail("not_summarized");
        }

        var vendor = await db.Vendors.AsNoTracking().FirstAsync(item => item.Id == document.VendorId, cancellationToken);
        var now = time.GetUtcNow();
        document.SignedOffAt = now;
        document.SignedOffBy = Actor();
        document.SignOffNote = note;
        await WriteSignOffEvidenceAsync(db, document, vendor.Name, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.document.signoff", $"{vendor.Name} {document.FileName}", cancellationToken);
        return Result<VendorReviewDocumentDto>.Ok(ToDocument(document, vendor.Name));
    }

    public async Task<Result<VendorReminderSyncResult>> SyncRemindersAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var today = Today();
        var vendors = await db.Vendors.AsNoTracking().ToListAsync(cancellationToken);
        var profiles = await db.VendorProfiles.AsNoTracking().ToDictionaryAsync(item => item.VendorId, cancellationToken);
        var open = await db.WorkTasks.Where(item => item.LinkedKind == "Vendor" && item.Status != "Done").ToListAsync(cancellationToken);
        var created = new List<WorkTask>();
        foreach (var vendor in vendors)
        {
            var row = ToRow(vendor, profiles.GetValueOrDefault(vendor.Id), null);
            if (row.ReviewStatus is "Overdue" or "DueSoon")
            {
                AddReminder(db, open, created, vendor.Name, ReviewTaskPrefix + vendor.Name, row, row.NextReviewOn);
            }

            if (row.RenewalState is "Upcoming" or "Lapsed" && row.ContractRenewalOn is DateOnly renewal)
            {
                AddReminder(db, open, created, vendor.Name, RenewalTaskPrefix + vendor.Name, row, renewal.AddDays(-30) < today ? today : renewal.AddDays(-30));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.reminders.sync", $"{created.Count} created", cancellationToken);
        var reminders = await db.WorkTasks.AsNoTracking()
            .Where(item => item.LinkedKind == "Vendor" && item.Status != "Done" && (item.Title.StartsWith(ReviewTaskPrefix) || item.Title.StartsWith(RenewalTaskPrefix)))
            .OrderBy(item => item.DueOn)
            .ToListAsync(cancellationToken);
        return Result<VendorReminderSyncResult>.Ok(new VendorReminderSyncResult(created.Count, reminders.Select(ToReminder).ToArray()));
    }

    private static void AddReminder(TenantDbContext db, List<WorkTask> open, List<WorkTask> created, string vendorName, string title, VendorRiskRow row, DateOnly due)
    {
        if (open.Any(item => item.LinkedCode == vendorName && item.Title == title) || created.Any(item => item.Title == title))
        {
            return;
        }

        var task = new WorkTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            LinkedKind = "Vendor",
            LinkedCode = vendorName,
            OwnerName = row.OwnerName,
            OwnerEmail = row.OwnerEmail,
            DueOn = due,
            Status = "Open",
            Recurrence = "None",
        };
        db.WorkTasks.Add(task);
        created.Add(task);
    }

    private Task<VendorReviewDocument> StoreAsync(
        TenantDbContext db,
        Guid tenantId,
        Guid vendorId,
        Guid? assessmentId,
        string kind,
        string fileName,
        byte[] bytes,
        string uploadedBy,
        CancellationToken cancellationToken) =>
        VendorDocumentStore.StoreAsync(db, storage, time, tenantId, vendorId, assessmentId, kind, fileName, bytes, uploadedBy, cancellationToken);

    private async Task SummarizeCoreAsync(VendorReviewDocument document, string vendorName, byte[] bytes, CancellationToken cancellationToken)
    {
        var text = Soc2Summarizer.ExtractText(bytes);
        var summary = Soc2Summarizer.Summarize(text);
        VendorRiskShared.ApplySummary(document, summary);
        var deterministic = VendorRiskShared.DeterministicText(vendorName, summary);
        var excerpt = VendorRiskShared.Trim(text, 6000);
        var facts = new List<AssistantFact>
        {
            new("vendor-document", document.FileName, $"{vendorName} SOC 2 report", excerpt, $"/vendors/{document.VendorId}"),
            new("vendor-summary", "parsed", "Parsed fields", deterministic, $"/vendors/{document.VendorId}"),
        };
        var reply = await assistant.AnswerAsync(
            "Summarize this vendor SOC 2 report for a reviewer. State the report period, the auditor opinion, every exception, and every complementary user entity control. Use only the supplied text.",
            facts,
            $"Vendor document review for {vendorName}",
            cancellationToken);
        if (reply.Mode == "llm" && !string.IsNullOrWhiteSpace(reply.Answer))
        {
            document.SummaryText = VendorRiskShared.Trim(reply.Answer.Trim(), 8000);
            document.SummaryMode = "ai";
        }
        else
        {
            document.SummaryText = deterministic;
            document.SummaryMode = "deterministic";
        }

        document.SummarizedAt = time.GetUtcNow();
    }

    private async Task WriteSignOffEvidenceAsync(TenantDbContext db, VendorReviewDocument document, string vendorName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var controlId = await db.ControlMappings.AsNoTracking()
            .Where(item => item.Requirement != null && VendorRiskShared.RequirementCodes.Contains(item.Requirement.Code))
            .Select(item => (Guid?)item.ControlId)
            .FirstOrDefaultAsync(cancellationToken);
        if (controlId is null)
        {
            return;
        }

        var retention = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstOrDefaultAsync(cancellationToken);
        retention = Math.Max(retention, DatabaseNames.MinimumRetentionYears);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "vendor_document_signoff_v1",
            vendor = vendorName,
            document = document.FileName,
            documentKind = document.Kind,
            documentSha256 = document.Sha256,
            reportPeriod = document.ReportPeriod,
            opinion = document.Opinion,
            exceptions = VendorRiskShared.Lines(document.Exceptions),
            cuecs = VendorRiskShared.Lines(document.Cuecs),
            summary = document.SummaryText,
            summaryMode = document.SummaryMode,
            signedOffBy = document.SignedOffBy,
            signedOffAt = now,
            note = document.SignOffNote,
        }, new JsonSerializerOptions { WriteIndented = true });
        var id = Guid.NewGuid();
        var item = new EvidenceItem
        {
            Id = id,
            ControlId = controlId.Value,
            Title = VendorRiskShared.Trim($"Vendor report reviewed: {vendorName} {document.Kind}", 300),
            Source = "Vendor risk",
            ObjectKey = $"{tenant.TenantId:N}/{id:N}.json",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(item);
        await evidence.AppendAsync(db, tenant.TenantId, item, payload, "application/json", now.AddYears(retention), cancellationToken);
    }

    private async Task<int> AddSuggestionsAsync(TenantDbContext db, IReadOnlyList<DiscoveredVendor> found, CancellationToken cancellationToken)
    {
        var known = (await db.VendorDiscoverySuggestions.AsNoTracking().Select(item => item.Name).ToListAsync(cancellationToken))
            .Concat(await db.Vendors.AsNoTracking().Select(item => item.Name).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = time.GetUtcNow();
        var added = 0;
        foreach (var item in found)
        {
            var name = VendorRiskShared.Trim(item.Name.Trim(), 200);
            if (name.Length == 0 || !known.Add(name))
            {
                continue;
            }

            db.VendorDiscoverySuggestions.Add(new VendorDiscoverySuggestion
            {
                Id = Guid.NewGuid(),
                Name = name,
                Source = VendorRiskShared.Trim(item.Source, 64),
                Detail = VendorRiskShared.Trim(item.Detail, 500),
                SuggestedCriticality = Enum.TryParse<VendorCriticality>(item.SuggestedCriticality, out var parsed) ? parsed.ToString() : "Medium",
                DataCategories = VendorRiskShared.Trim(string.Join(',', item.DataCategories), 500),
                Status = "New",
                FirstSeenAt = now,
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task EnsureExampleAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        if (!tenant.IsExample || await db.VendorProfiles.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        var today = Today();
        var vendors = await db.Vendors.ToListAsync(cancellationToken);
        if (vendors.Count == 0)
        {
            return;
        }

        var owners = new[] { ("Priya Nair", "priya.nair@harborline.example"), ("Lena Cho", "lena.cho@harborline.example"), ("Marcus Lee", "marcus.lee@harborline.example") };
        var index = 0;
        foreach (var vendor in vendors.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var (ownerName, ownerEmail) = owners[index % owners.Length];
            var categories = VendorRiskScoring.InferCategories(vendor.DataAccess);
            var risk = VendorRiskScoring.Inherent(vendor.Criticality, categories, vendor.Criticality >= VendorCriticality.High);
            db.VendorProfiles.Add(new VendorProfile
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                OwnerName = ownerName,
                OwnerEmail = ownerEmail,
                ContractRenewalOn = today.AddDays(20 + (index * 37)),
                DataCategories = string.Join(',', categories),
                Subprocessor = vendor.Criticality >= VendorCriticality.High,
                ReviewCadenceMonths = risk.DefaultCadenceMonths,
                LastReviewedOn = vendor.ReviewState == VendorReviewState.Approved ? today.AddMonths(-3) : null,
                ContactName = $"{vendor.Name} security team",
                ContactEmail = $"security@{Slug(vendor.Name)}.example",
                UpdatedAt = now,
            });
            index++;
        }

        await AddSuggestionsAsync(db, MockVendorDiscoverySource.Fixtures, cancellationToken);
        var pagerly = vendors.FirstOrDefault(item => item.Name == "Pagerly");
        if (pagerly is not null)
        {
            var assessment = new VendorAssessment
            {
                Id = Guid.NewGuid(),
                VendorId = pagerly.Id,
                VendorName = pagerly.Name,
                TemplateCode = VendorQuestionnaireLibrary.Core.Code,
                ContactName = "Rin Okafor",
                ContactEmail = "security@pagerly.example",
                TokenHash = VendorPortalTokens.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                ExpiresAt = now.AddDays(9),
                Status = "InProgress",
                SentAt = now.AddDays(-5),
                SentBy = "Priya Nair",
                DeliveryReceipt = "example",
            };
            var answers = new[] { "Yes", "Yes", "Partial", "Yes", "Yes", "Yes", "No", "Yes", "Yes" };
            for (var i = 0; i < answers.Length; i++)
            {
                assessment.Answers.Add(new VendorAssessmentAnswer
                {
                    Id = Guid.NewGuid(),
                    AssessmentId = assessment.Id,
                    QuestionCode = VendorQuestionnaireLibrary.Core.Questions[i].Code,
                    Answer = answers[i],
                    Comment = answers[i] == "No" ? "Rolling out hardware keys next quarter." : "",
                    UpdatedAt = now.AddDays(-2),
                });
            }

            db.VendorAssessments.Add(assessment);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, VendorAssessment>> LatestAssessmentsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.VendorAssessments.AsNoTracking().OrderByDescending(item => item.SentAt).ToListAsync(cancellationToken);
        return rows.GroupBy(item => item.VendorId).ToDictionary(group => group.Key, group => group.First());
    }

    private VendorRiskRow ToRow(VendorRecord vendor, VendorProfile? profile, VendorAssessment? latest)
    {
        var today = Today();
        var categories = profile is null ? VendorRiskScoring.InferCategories(vendor.DataAccess) : LifecycleRules.Split(profile.DataCategories);
        var subprocessor = profile?.Subprocessor ?? false;
        var risk = VendorRiskScoring.Inherent(vendor.Criticality, categories, subprocessor);
        var cadence = profile?.ReviewCadenceMonths ?? risk.DefaultCadenceMonths;
        var review = VendorRiskScoring.Review(profile?.LastReviewedOn, cadence, vendor.ReviewDueOn, today);
        return new VendorRiskRow(
            vendor.Id,
            vendor.Name,
            vendor.Service,
            vendor.Criticality.ToString(),
            vendor.DataAccess,
            vendor.ReviewState.ToString(),
            vendor.Decision,
            risk.Score,
            risk.Tier,
            profile?.OwnerName ?? "",
            profile?.OwnerEmail ?? "",
            profile?.ContractRenewalOn,
            VendorRiskScoring.RenewalState(profile?.ContractRenewalOn, today),
            categories,
            subprocessor,
            cadence,
            profile?.LastReviewedOn,
            review.NextReviewOn,
            review.State,
            profile?.ContactName ?? "",
            profile?.ContactEmail ?? "",
            latest is null ? "" : VendorRiskShared.EffectiveStatus(latest, time.GetUtcNow()),
            latest?.Score);
    }

    private VendorAssessmentDto ToAssessment(VendorAssessment item)
    {
        var template = VendorQuestionnaireLibrary.Find(item.TemplateCode) ?? VendorQuestionnaireLibrary.Core;
        var score = VendorQuestionnaireLibrary.Score(template, item.Answers.ToDictionary(answer => answer.QuestionCode, answer => answer.Answer, StringComparer.Ordinal));
        return new VendorAssessmentDto(
            item.Id,
            item.VendorId,
            item.VendorName,
            item.TemplateCode,
            template.Name,
            item.ContactName,
            item.ContactEmail,
            VendorRiskShared.EffectiveStatus(item, time.GetUtcNow()),
            item.SentAt,
            item.SentBy,
            item.ExpiresAt,
            item.SubmittedAt,
            item.ReviewedAt,
            item.ReviewedBy,
            item.ReviewNote,
            item.Score,
            item.Rating,
            score.Answered,
            score.Total,
            score.Flagged,
            item.DeliveryReceipt,
            item.Answers.OrderBy(answer => template.Questions.ToList().FindIndex(question => question.Code == answer.QuestionCode))
                .Select(answer => new VendorAnswerDto(answer.QuestionCode, answer.Answer, answer.Comment))
                .ToArray());
    }

    internal static QuestionnaireTemplateDto ToTemplate(QuestionnaireTemplate item) =>
        new(item.Code, item.Name, item.Description, item.Questions.Select(question => new QuestionnaireQuestionDto(question.Code, question.Domain, question.Text, question.Weight, question.Critical)).ToArray());

    private static VendorReviewDocumentDto ToDocument(VendorReviewDocument item, string vendorName) =>
        new(
            item.Id,
            item.VendorId,
            vendorName,
            item.AssessmentId,
            item.Kind,
            item.FileName,
            item.SizeBytes,
            item.Sha256,
            item.UploadedAt,
            item.UploadedBy,
            item.ReportPeriod,
            item.Opinion,
            VendorRiskShared.Lines(item.Exceptions),
            VendorRiskShared.Lines(item.Cuecs),
            item.SummaryText,
            item.SummaryMode,
            item.SummarizedAt,
            item.SignedOffBy,
            item.SignedOffAt,
            item.SignOffNote);

    private static VendorDiscoveryDto ToDiscovery(VendorDiscoverySuggestion item) =>
        new(item.Id, item.Name, item.Source, item.Detail, item.SuggestedCriticality, LifecycleRules.Split(item.DataCategories), item.Status, item.FirstSeenAt, item.DecidedAt, item.DecidedBy, item.VendorId);

    private static VendorReminderDto ToReminder(WorkTask item) =>
        new(item.Id, item.Title, item.LinkedCode, item.DueOn, item.Status);

    private string PortalLink(string path)
    {
        var baseUrl = (configuration["PORTAL_BASE_URL"] ?? "").Trim().TrimEnd('/');
        return baseUrl.Length > 0 && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? baseUrl + path
            : path;
    }

    private static bool OptionalEmail(string value) =>
        value.Length == 0 || (value.Length <= 320 && value.Count(ch => ch == '@') == 1 && value.IndexOf('@') > 0 && value.IndexOf('@') < value.Length - 1 && !value.Any(char.IsWhiteSpace));

    private static string Slug(string name) =>
        new(name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private string Actor() => string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Email : tenant.DisplayName;

    private TenantDbContext OpenDb() => databases.Create(tenant.DatabaseName);

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", VendorRiskShared.Trim(detail, 500), null, cancellationToken);
}

public static class VendorDocumentStore
{
    public static async Task<VendorReviewDocument> StoreAsync(
        TenantDbContext db,
        IStorageProvider storage,
        TimeProvider time,
        Guid tenantId,
        Guid vendorId,
        Guid? assessmentId,
        string kind,
        string fileName,
        byte[] bytes,
        string uploadedBy,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var contentType = VendorRiskShared.ContentTypeFor(fileName);
        var key = $"{tenantId:N}/vendor-documents/{id:N}";
        await storage.PutAsync(key, bytes, contentType, null, cancellationToken);
        var document = new VendorReviewDocument
        {
            Id = id,
            VendorId = vendorId,
            AssessmentId = assessmentId,
            Kind = kind,
            FileName = fileName,
            ContentType = contentType,
            ObjectKey = key,
            Sha256 = VendorRiskShared.Sha256(bytes),
            SizeBytes = bytes.Length,
            UploadedAt = time.GetUtcNow(),
            UploadedBy = VendorRiskShared.Trim(uploadedBy, 200),
        };
        db.VendorReviewDocuments.Add(document);
        return document;
    }
}
