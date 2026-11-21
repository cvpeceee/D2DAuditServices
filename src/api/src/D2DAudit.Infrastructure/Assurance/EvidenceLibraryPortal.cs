using System.Globalization;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

public sealed class EvidenceLibraryPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    TimeProvider time) : IEvidenceLibraryPortal
{
    private const int MaxUploadBytes = 750_000;
    private const int MaxBulk = 100;

    public async Task<EvidenceLibraryPage> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var controls = await AssuranceScope.ControlsAsync(db, cancellationToken);
        var documents = await Documents(db).AsNoTracking().ToListAsync(cancellationToken);
        var today = Today();
        var visible = documents
            .Where(item => Visible(item, controls))
            .Select(item => ToDto(item, today))
            .ToArray();
        var mine = documents
            .SelectMany(item => item.Requests.Select(request => (Document: item, Request: request)))
            .Where(pair => pair.Request.Status == EvidenceRequestRules.Open && string.Equals(pair.Request.AssigneeEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
            .Select(pair => ToRequest(pair.Document, pair.Request, today))
            .ToArray();
        var summary = new EvidenceLibrarySummary(
            visible.Length,
            visible.Count(item => item.Status == EvidenceExpiry.Current),
            visible.Count(item => item.Status == EvidenceExpiry.ExpiringSoon),
            visible.Count(item => item.Status == EvidenceExpiry.Expired),
            visible.Count(item => item.Status == EvidenceExpiry.Missing),
            visible.Sum(item => item.Requests.Count(request => request.Status == EvidenceRequestRules.Open)));
        return new EvidenceLibraryPage(summary, visible, mine);
    }

    public async Task<Result<EvidenceDocumentDto>> CreateAsync(CreateEvidenceDocumentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 || (request.Description ?? "").Length > 2000)
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_document");
        }

        if (!Enum.TryParse<EvidenceCadence>(request.Cadence, ignoreCase: false, out var cadence) || !Enum.IsDefined(cadence))
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_cadence");
        }

        var ownerEmail = (request.OwnerEmail ?? "").Trim();
        if (ownerEmail.Length > 320 || (ownerEmail.Length > 0 && !ownerEmail.Contains('@')) || (request.OwnerName ?? "").Trim().Length > 200)
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_owner");
        }

        await using var db = Open();
        var codes = Codes(request.ControlCodes);
        if (codes.Length > MaxBulk || !await ControlsExistAsync(db, codes, cancellationToken))
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_controls");
        }

        var count = await db.EvidenceDocuments.CountAsync(cancellationToken);
        var existing = await db.EvidenceDocuments.Select(item => item.Code).ToListAsync(cancellationToken);
        var number = count + 1;
        while (existing.Contains(DocumentCode(number), StringComparer.Ordinal))
        {
            number++;
        }

        var document = new EvidenceDocument
        {
            Id = Guid.NewGuid(),
            Code = DocumentCode(number),
            Title = request.Title.Trim(),
            Description = (request.Description ?? "").Trim(),
            OwnerName = string.IsNullOrWhiteSpace(request.OwnerName) ? AssuranceScope.Actor(tenant) : request.OwnerName.Trim(),
            OwnerEmail = ownerEmail.Length == 0 ? tenant.Email : ownerEmail,
            Cadence = cadence,
            CreatedAt = time.GetUtcNow(),
            Links = [.. codes.Select(code => new EvidenceDocumentLink { ControlCode = code })],
        };
        db.EvidenceDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshControlsAsync(db, codes, cancellationToken);
        await WriteAsync("evidence.library.created", document.Code, cancellationToken);
        return Result<EvidenceDocumentDto>.Ok(ToDto(document, Today()));
    }

    public async Task<Result<int>> LinkAsync(BulkLinkEvidenceRequest request, CancellationToken cancellationToken)
    {
        var documentCodes = Codes(request.DocumentCodes);
        var controlCodes = Codes(request.ControlCodes);
        if (documentCodes.Length is 0 or > MaxBulk || controlCodes.Length is 0 or > MaxBulk)
        {
            return Result<int>.Fail("invalid_link");
        }

        await using var db = Open();
        if (!await ControlsExistAsync(db, controlCodes, cancellationToken))
        {
            return Result<int>.Fail("invalid_controls");
        }

        var documents = await db.EvidenceDocuments.Include(item => item.Links).Where(item => documentCodes.Contains(item.Code)).ToListAsync(cancellationToken);
        if (documents.Count != documentCodes.Length)
        {
            return Result<int>.Fail("not_found");
        }

        var added = 0;
        foreach (var document in documents)
        {
            foreach (var code in controlCodes.Where(code => document.Links.All(link => link.ControlCode != code)))
            {
                var link = new EvidenceDocumentLink { DocumentId = document.Id, ControlCode = code };
                document.Links.Add(link);
                db.EvidenceDocumentLinks.Add(link);
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshControlsAsync(db, controlCodes, cancellationToken);
        await WriteAsync("evidence.library.linked", $"{added} link(s): {string.Join(",", documentCodes)} -> {string.Join(",", controlCodes)}", cancellationToken);
        return Result<int>.Ok(added);
    }

    public async Task<Result<EvidenceDocumentDto>> RequestAsync(string code, CreateEvidenceRequestRequest request, CancellationToken cancellationToken)
    {
        var today = Today();
        var rule = EvidenceRequestRules.Validate(request.AssigneeName, request.AssigneeEmail, request.DueOn, today);
        if (!rule.Succeeded)
        {
            return Result<EvidenceDocumentDto>.Fail(rule.Error!);
        }

        if ((request.Note ?? "").Length > 1000)
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_note");
        }

        await using var db = Open();
        var document = await Documents(db).FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (document is null)
        {
            return Result<EvidenceDocumentDto>.Fail("not_found");
        }

        var row = new EvidenceRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            AssigneeName = request.AssigneeName.Trim(),
            AssigneeEmail = request.AssigneeEmail.Trim(),
            DueOn = request.DueOn,
            Note = (request.Note ?? "").Trim(),
            Status = EvidenceRequestRules.Open,
            RequestedByName = AssuranceScope.Actor(tenant),
            RequestedAt = time.GetUtcNow(),
        };
        document.Requests.Add(row);
        db.EvidenceRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("evidence.library.requested", $"{document.Code} from {row.AssigneeEmail}", cancellationToken);
        return Result<EvidenceDocumentDto>.Ok(ToDto(document, today));
    }

    public async Task<Result<EvidenceDocumentDto>> UploadAsync(string code, UploadLibraryEvidenceRequest request, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<EvidenceDocumentDto>.Fail("forbidden");
        }

        var fileName = Path.GetFileName((request.FileName ?? "").Trim());
        if (fileName.Length is 0 or > 200 || string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_evidence");
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_evidence");
        }

        if (body.Length is 0 or > MaxUploadBytes)
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_evidence");
        }

        var today = Today();
        var validFrom = request.ValidFrom ?? today;
        if (validFrom > today || validFrom < today.AddYears(-1))
        {
            return Result<EvidenceDocumentDto>.Fail("invalid_valid_from");
        }

        await using var db = Open();
        var document = await Documents(db).FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (document is null)
        {
            return Result<EvidenceDocumentDto>.Fail("not_found");
        }

        var open = document.Requests
            .Where(item => item.Status == EvidenceRequestRules.Open && string.Equals(item.AssigneeEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!tenant.CanWrite && open.Length == 0)
        {
            return Result<EvidenceDocumentDto>.Fail("forbidden");
        }

        var codes = document.Links.Select(item => item.ControlCode).ToArray();
        var controls = await db.Controls.Where(item => codes.Contains(item.Code)).ToListAsync(cancellationToken);
        if (controls.Count == 0)
        {
            return Result<EvidenceDocumentDto>.Fail("document_unlinked");
        }

        var retention = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstAsync(cancellationToken);
        var now = time.GetUtcNow();
        EvidenceItem? first = null;
        foreach (var controlItem in controls.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            var id = Guid.NewGuid();
            var item = new EvidenceItem
            {
                Id = id,
                ControlId = controlItem.Id,
                Title = $"{document.Title} ({fileName})",
                Source = "Library " + document.Code,
                ObjectKey = tenant.TenantId.ToString("N") + "/" + id.ToString("N") + ".bin",
                Integrity = EvidenceIntegrity.NotStored,
            };
            db.Evidence.Add(item);
            await evidence.AppendAsync(db, tenant.TenantId, item, body, "application/octet-stream", now.AddYears(retention), cancellationToken);
            first ??= item;
        }

        document.CurrentEvidenceId = first!.Id;
        document.CurrentSha256 = first.Sha256;
        document.ValidFrom = validFrom;
        document.ValidUntil = EvidenceExpiry.NextValidUntil(validFrom, document.Cadence);
        foreach (var request2 in document.Requests.Where(item => item.Status == EvidenceRequestRules.Open))
        {
            request2.Status = EvidenceRequestRules.Fulfilled;
            request2.FulfilledAt = now;
        }

        var tasks = await db.WorkTasks.Where(item => item.LinkedKind == "Evidence" && item.LinkedCode == document.Code && item.Status == "Open").ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.Status = "Done";
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshControlsAsync(db, codes, cancellationToken);
        await WriteAsync("evidence.library.uploaded", $"{document.Code} sha256 {document.CurrentSha256}", cancellationToken);
        return Result<EvidenceDocumentDto>.Ok(ToDto(document, today));
    }

    public async Task<Result<int>> CreateRenewalTasksAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var today = Today();
        var documents = await db.EvidenceDocuments.AsNoTracking().ToListAsync(cancellationToken);
        var open = await db.WorkTasks.AsNoTracking()
            .Where(item => item.LinkedKind == "Evidence" && item.Status == "Open")
            .Select(item => item.LinkedCode)
            .ToListAsync(cancellationToken);
        var created = 0;
        foreach (var document in documents)
        {
            var status = EvidenceExpiry.Status(document.CurrentEvidenceId is not null, document.ValidUntil, today);
            if (!EvidenceExpiry.NeedsRenewal(status) || open.Contains(document.Code, StringComparer.Ordinal))
            {
                continue;
            }

            var due = document.ValidUntil is DateOnly until && until > today ? until : today.AddDays(14);
            db.WorkTasks.Add(new WorkTask
            {
                Id = Guid.NewGuid(),
                Title = "Renew evidence: " + document.Title,
                LinkedKind = "Evidence",
                LinkedCode = document.Code,
                OwnerName = document.OwnerName,
                OwnerEmail = document.OwnerEmail,
                DueOn = due,
                Status = "Open",
                Recurrence = "None",
            });
            created++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("evidence.library.renewals", created.ToString(CultureInfo.InvariantCulture), cancellationToken);
        return Result<int>.Ok(created);
    }

    private async Task RefreshControlsAsync(TenantDbContext db, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (codes.Count == 0)
        {
            return;
        }

        var controls = await db.Controls.Include(item => item.Tests).Where(item => codes.Contains(item.Code)).ToListAsync(cancellationToken);
        foreach (var controlItem in controls)
        {
            controlItem.Status = CollectionRunner.Recompute(controlItem);
        }

        await AssuranceCycle.ApplyLibraryAsync(db, controls, Today(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static EvidenceDocumentDto ToDto(EvidenceDocument item, DateOnly today) => new(
        item.Id,
        item.Code,
        item.Title,
        item.Description,
        item.OwnerName,
        item.OwnerEmail,
        item.Cadence.ToString(),
        item.ValidFrom,
        item.ValidUntil,
        EvidenceExpiry.Status(item.CurrentEvidenceId is not null, item.ValidUntil, today),
        item.CurrentEvidenceId,
        item.CurrentSha256,
        item.Links.Select(link => link.ControlCode).Order(StringComparer.Ordinal).ToArray(),
        item.Requests.OrderByDescending(request => request.RequestedAt).Select(request => ToRequest(item, request, today)).ToArray());

    private static EvidenceRequestDto ToRequest(EvidenceDocument document, EvidenceRequest item, DateOnly today) => new(
        item.Id,
        document.Code,
        document.Title,
        item.AssigneeName,
        item.AssigneeEmail,
        item.DueOn,
        item.Note,
        item.Status,
        EvidenceRequestRules.IsOverdue(item.Status, item.DueOn, today),
        item.RequestedByName,
        item.RequestedAt,
        item.FulfilledAt);

    private bool Visible(EvidenceDocument document, IReadOnlyDictionary<string, Control> controls) =>
        !tenant.IsAuditor || document.Links.Any(link => controls.TryGetValue(link.ControlCode, out var item) && AssuranceScope.InScope(tenant, item));

    private static IQueryable<EvidenceDocument> Documents(TenantDbContext db) =>
        db.EvidenceDocuments.Include(item => item.Links).Include(item => item.Requests).AsSplitQuery().OrderBy(item => item.Code);

    private static async Task<bool> ControlsExistAsync(TenantDbContext db, string[] codes, CancellationToken cancellationToken) =>
        codes.Length == 0 || await db.Controls.CountAsync(item => codes.Contains(item.Code), cancellationToken) == codes.Length;

    private static string[] Codes(IReadOnlyList<string>? codes) =>
        (codes ?? []).Select(code => (code ?? "").Trim()).Where(code => code.Length is > 0 and <= 32).Distinct(StringComparer.Ordinal).ToArray();

    private static string DocumentCode(int number) => "EVD-" + number.ToString("000", CultureInfo.InvariantCulture);

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private TenantDbContext Open() => databases.Create(AssuranceScope.Database(tenant));

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
