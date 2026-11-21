using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.VendorRisk;

/// <summary>Unauthenticated vendor questionnaire portal. The token names the tenant and carries a secret whose hash is compared in constant time.</summary>
public sealed class VendorPortalPublic(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    IStorageProvider storage,
    IActivityWriter activity,
    TimeProvider time) : IVendorPortal
{
    private const int MaxCommentLength = 2000;

    public async Task<Result<VendorPortalView>> GetAsync(string token, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.Error is not null)
        {
            return Result<VendorPortalView>.Fail(opened.Error);
        }

        await using var db = opened.Db!;
        return Result<VendorPortalView>.Ok(await ViewAsync(db, opened.TenantName, opened.Assessment!, cancellationToken));
    }

    public async Task<Result<VendorPortalView>> SaveAnswersAsync(string token, VendorPortalAnswersRequest request, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.Error is not null)
        {
            return Result<VendorPortalView>.Fail(opened.Error);
        }

        await using var db = opened.Db!;
        var assessment = opened.Assessment!;
        var now = time.GetUtcNow();
        if (!VendorRiskShared.Editable(VendorRiskShared.EffectiveStatus(assessment, now)))
        {
            return Result<VendorPortalView>.Fail("locked");
        }

        var template = VendorQuestionnaireLibrary.Find(assessment.TemplateCode) ?? VendorQuestionnaireLibrary.Core;
        var codes = template.Questions.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
        var incoming = request.Answers ?? [];
        if (incoming.Count > template.Questions.Count
            || incoming.Any(item => item is null || !codes.Contains(item.QuestionCode ?? "") || !VendorQuestionnaireLibrary.ValidAnswer(item.Answer) || (item.Comment ?? "").Length > MaxCommentLength)
            || incoming.Select(item => item.QuestionCode).Distinct(StringComparer.Ordinal).Count() != incoming.Count)
        {
            return Result<VendorPortalView>.Fail("invalid_answers");
        }

        foreach (var item in incoming)
        {
            var existing = assessment.Answers.FirstOrDefault(answer => answer.QuestionCode == item.QuestionCode);
            if (existing is null)
            {
                existing = new VendorAssessmentAnswer { Id = Guid.NewGuid(), AssessmentId = assessment.Id, QuestionCode = item.QuestionCode };
                assessment.Answers.Add(existing);
                db.VendorAssessmentAnswers.Add(existing);
            }

            existing.Answer = item.Answer;
            existing.Comment = (item.Comment ?? "").Trim();
            existing.UpdatedAt = now;
        }

        var answers = assessment.Answers.ToDictionary(item => item.QuestionCode, item => item.Answer, StringComparer.Ordinal);
        if (request.Submit)
        {
            var score = VendorQuestionnaireLibrary.Score(template, answers);
            if (score.Answered < score.Total)
            {
                return Result<VendorPortalView>.Fail("incomplete");
            }

            assessment.Status = "Submitted";
            assessment.SubmittedAt = now;
            assessment.Score = score.Percent;
            assessment.Rating = score.Rating;
        }
        else
        {
            assessment.Status = "InProgress";
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, opened.TenantId, request.Submit ? "vendor.portal.submit" : "vendor.portal.save", "ok", VendorRiskShared.Trim($"{assessment.VendorName} {answers.Count}/{template.Questions.Count}", 500), null, cancellationToken);
        return Result<VendorPortalView>.Ok(await ViewAsync(db, opened.TenantName, assessment, cancellationToken));
    }

    public async Task<Result<VendorPortalView>> UploadAsync(string token, UploadVendorDocumentRequest request, CancellationToken cancellationToken)
    {
        var fileName = VendorRiskShared.CleanFileName(request.FileName);
        var bytes = VendorRiskShared.Decode(request.ContentBase64);
        if (!VendorRiskShared.DocumentKinds.Contains(request.Kind ?? "", StringComparer.Ordinal) || fileName.Length == 0 || bytes is null)
        {
            return Result<VendorPortalView>.Fail("invalid_document");
        }

        var opened = await OpenAsync(token, cancellationToken);
        if (opened.Error is not null)
        {
            return Result<VendorPortalView>.Fail(opened.Error);
        }

        await using var db = opened.Db!;
        var assessment = opened.Assessment!;
        if (!VendorRiskShared.Editable(VendorRiskShared.EffectiveStatus(assessment, time.GetUtcNow())))
        {
            return Result<VendorPortalView>.Fail("locked");
        }

        if (await db.VendorReviewDocuments.CountAsync(item => item.AssessmentId == assessment.Id, cancellationToken) >= VendorRiskShared.MaxDocumentsPerAssessment)
        {
            return Result<VendorPortalView>.Fail("too_many_documents");
        }

        var document = await VendorDocumentStore.StoreAsync(db, storage, time, opened.TenantId, assessment.VendorId, assessment.Id, request.Kind!, fileName, bytes, $"{assessment.ContactName} (vendor)", cancellationToken);
        if (document.Kind == "Soc2")
        {
            var summary = Soc2Summarizer.Summarize(Soc2Summarizer.ExtractText(bytes));
            VendorRiskShared.ApplySummary(document, summary);
            document.SummaryText = VendorRiskShared.DeterministicText(assessment.VendorName, summary);
            document.SummaryMode = "deterministic";
            document.SummarizedAt = time.GetUtcNow();
        }

        if (assessment.Status == "Sent")
        {
            assessment.Status = "InProgress";
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, opened.TenantId, "vendor.portal.upload", "ok", VendorRiskShared.Trim($"{assessment.VendorName} {document.Kind} {document.FileName}", 500), null, cancellationToken);
        return Result<VendorPortalView>.Ok(await ViewAsync(db, opened.TenantName, assessment, cancellationToken));
    }

    private async Task<Opened> OpenAsync(string token, CancellationToken cancellationToken)
    {
        if (!VendorPortalTokens.TryParse(token, out var tenantId, out var secret))
        {
            return Opened.Failed("not_found");
        }

        var tenantRow = await control.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenantId && item.Status == TenantStatus.Active && item.DatabaseName != null, cancellationToken);
        if (tenantRow is null || !DatabaseNames.IsSafe(tenantRow.DatabaseName!))
        {
            return Opened.Failed("not_found");
        }

        var hash = VendorPortalTokens.Hash(secret);
        var db = databases.Create(tenantRow.DatabaseName!);
        var assessment = await db.VendorAssessments.Include(item => item.Answers).FirstOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        var state = assessment is null ? PortalTokenState.Invalid : VendorPortalTokens.Verify(assessment.TokenHash, secret, assessment.ExpiresAt, time.GetUtcNow());
        if (assessment is null || state == PortalTokenState.Invalid || assessment.Status == "Revoked")
        {
            await db.DisposeAsync();
            return Opened.Failed("not_found");
        }

        if (state == PortalTokenState.Expired && VendorRiskShared.Editable(assessment.Status))
        {
            await db.DisposeAsync();
            return Opened.Failed("expired");
        }

        return new Opened(null, tenantId, tenantRow.Name, db, assessment);
    }

    private async Task<VendorPortalView> ViewAsync(TenantDbContext db, string tenantName, VendorAssessment assessment, CancellationToken cancellationToken)
    {
        var template = VendorQuestionnaireLibrary.Find(assessment.TemplateCode) ?? VendorQuestionnaireLibrary.Core;
        var documents = await db.VendorReviewDocuments.AsNoTracking()
            .Where(item => item.AssessmentId == assessment.Id)
            .OrderBy(item => item.UploadedAt)
            .Select(item => new VendorPortalDocument(item.Kind, item.FileName, item.UploadedAt))
            .ToListAsync(cancellationToken);
        return new VendorPortalView(
            tenantName,
            assessment.VendorName,
            assessment.ContactName,
            VendorRiskShared.EffectiveStatus(assessment, time.GetUtcNow()),
            assessment.ExpiresAt,
            VendorRiskPortal.ToTemplate(template),
            assessment.Answers.Select(item => new VendorAnswerDto(item.QuestionCode, item.Answer, item.Comment)).ToArray(),
            documents);
    }

    private sealed record Opened(string? Error, Guid TenantId, string TenantName, TenantDbContext? Db, VendorAssessment? Assessment)
    {
        public static Opened Failed(string error) => new(error, Guid.Empty, "", null, null);
    }
}
