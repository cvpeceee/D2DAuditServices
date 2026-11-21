using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

public sealed class ControlInsightPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IControlInsightPortal
{
    public async Task<Result<ControlReadinessDto>> ReadinessAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var controlItem = await FindAsync(db, code, cancellationToken);
        if (controlItem is null)
        {
            return Result<ControlReadinessDto>.Fail("not_found");
        }

        return Result<ControlReadinessDto>.Ok(await BuildAsync(db, controlItem, cancellationToken));
    }

    public async Task<Result<ControlReadinessDto>> UpdateProfileAsync(string code, UpdateControlProfileRequest request, CancellationToken cancellationToken)
    {
        if ((request.ImplementationGuidance ?? "").Length > 4000
            || (request.EvidenceRequirement ?? "").Length > 2000
            || request.RequiredEvidenceCount is < 0 or > 50)
        {
            return Result<ControlReadinessDto>.Fail("invalid_profile");
        }

        await using var db = Open();
        var controlItem = await FindAsync(db, code, cancellationToken);
        if (controlItem is null)
        {
            return Result<ControlReadinessDto>.Fail("not_found");
        }

        var profile = await db.ControlProfiles.FirstOrDefaultAsync(item => item.ControlCode == controlItem.Code, cancellationToken);
        if (profile is null)
        {
            profile = new ControlProfile { ControlCode = controlItem.Code };
            db.ControlProfiles.Add(profile);
        }

        profile.ImplementationGuidance = (request.ImplementationGuidance ?? "").Trim();
        profile.EvidenceRequirement = (request.EvidenceRequirement ?? "").Trim();
        profile.RequiredEvidenceCount = request.RequiredEvidenceCount;
        profile.RequiresPassingTest = request.RequiresPassingTest;
        profile.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("control.profile.updated", controlItem.Code, cancellationToken);
        return Result<ControlReadinessDto>.Ok(await BuildAsync(db, controlItem, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<ControlCommentDto>>> CommentsAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var controlItem = await FindAsync(db, code, cancellationToken);
        if (controlItem is null)
        {
            return Result<IReadOnlyList<ControlCommentDto>>.Fail("not_found");
        }

        var rows = await db.ControlComments.AsNoTracking()
            .Include(item => item.Revisions)
            .Where(item => item.ControlCode == controlItem.Code)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ControlCommentDto>>.Ok(rows.Select(ToDto).ToArray());
    }

    public async Task<Result<ControlCommentDto>> AddCommentAsync(string code, ControlCommentRequest request, CancellationToken cancellationToken)
    {
        var rule = ControlReadiness.ValidateComment(request.Body);
        if (!rule.Succeeded)
        {
            return Result<ControlCommentDto>.Fail(rule.Error!);
        }

        await using var db = Open();
        var controlItem = await FindAsync(db, code, cancellationToken);
        if (controlItem is null)
        {
            return Result<ControlCommentDto>.Fail("not_found");
        }

        var comment = new ControlComment
        {
            Id = Guid.NewGuid(),
            ControlCode = controlItem.Code,
            AuthorName = AssuranceScope.Actor(tenant),
            AuthorEmail = tenant.Email,
            Body = request.Body.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        db.ControlComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("control.comment.added", controlItem.Code, cancellationToken);
        return Result<ControlCommentDto>.Ok(ToDto(comment));
    }

    public async Task<Result<ControlCommentDto>> EditCommentAsync(string code, Guid id, ControlCommentRequest request, CancellationToken cancellationToken)
    {
        var rule = ControlReadiness.ValidateComment(request.Body);
        if (!rule.Succeeded)
        {
            return Result<ControlCommentDto>.Fail(rule.Error!);
        }

        await using var db = Open();
        var comment = await db.ControlComments.Include(item => item.Revisions).FirstOrDefaultAsync(item => item.Id == id && item.ControlCode == code, cancellationToken);
        if (comment is null)
        {
            return Result<ControlCommentDto>.Fail("not_found");
        }

        if (!tenant.IsAdmin && !string.Equals(comment.AuthorEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ControlCommentDto>.Fail("forbidden");
        }

        var body = request.Body.Trim();
        if (body != comment.Body)
        {
            var now = time.GetUtcNow();
            var revision = new ControlCommentRevision { Id = Guid.NewGuid(), CommentId = comment.Id, Body = comment.Body, ReplacedAt = now };
            comment.Revisions.Add(revision);
            db.Add(revision);
            comment.Body = body;
            comment.EditedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await WriteAsync("control.comment.edited", comment.ControlCode, cancellationToken);
        }

        return Result<ControlCommentDto>.Ok(ToDto(comment));
    }

    private async Task<ControlReadinessDto> BuildAsync(TenantDbContext db, Control controlItem, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var profile = await db.ControlProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.ControlCode == controlItem.Code, cancellationToken)
            ?? new ControlProfile { ControlCode = controlItem.Code };
        var documents = await db.EvidenceDocuments.AsNoTracking()
            .Include(item => item.Links)
            .Include(item => item.Requests)
            .AsSplitQuery()
            .Where(item => item.Links.Any(link => link.ControlCode == controlItem.Code))
            .ToListAsync(cancellationToken);
        var library = documents.Select(item => EvidenceLibraryPortal.ToDto(item, today)).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        var report = ControlReadiness.Evaluate(new ReadinessInput(
            controlItem.OwnerEmail,
            controlItem.Tests.Select(item => item.Outcome).ToArray(),
            controlItem.Evidence.Count,
            profile.RequiredEvidenceCount,
            profile.RequiresPassingTest,
            library.Select(item => item.Status).ToArray()));
        return new ControlReadinessDto(
            controlItem.Code,
            report.Ready,
            report.Percent,
            report.Checks.Select(item => new ReadinessCheckDto(item.Key, item.Met, item.Detail)).ToArray(),
            new ControlProfileDto(profile.ImplementationGuidance, profile.EvidenceRequirement, profile.RequiredEvidenceCount, profile.RequiresPassingTest),
            library);
    }

    private async Task<Control?> FindAsync(TenantDbContext db, string code, CancellationToken cancellationToken)
    {
        var controlItem = await db.Controls.AsNoTracking()
            .Include(item => item.Tests)
            .Include(item => item.Evidence)
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        return controlItem is not null && AssuranceScope.InScope(tenant, controlItem) ? controlItem : null;
    }

    private static ControlCommentDto ToDto(ControlComment item) => new(
        item.Id,
        item.AuthorName,
        item.AuthorEmail,
        item.Body,
        item.CreatedAt,
        item.EditedAt,
        item.Revisions.OrderByDescending(revision => revision.ReplacedAt).Select(revision => new ControlCommentRevisionDto(revision.Body, revision.ReplacedAt)).ToArray());

    private TenantDbContext Open() => databases.Create(AssuranceScope.Database(tenant));

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
