using System.Globalization;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Audits;

public sealed class AuditPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IActivityWriter activity,
    IStorageProvider storage,
    CollectionJobs jobs,
    AuditExportQueue queue,
    TimeProvider time) : IAuditPortal
{
    public async Task<AuditPortalPage> GetAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            await WriteAsync("audit.portal.viewed", "portal", cancellationToken);
        }

        await using var db = Open();
        var engagement = await EngagementAsync(db, cancellationToken);
        if (engagement is null)
        {
            return new AuditPortalPage(null, [], [], []);
        }

        var controls = await ScopedControlsAsync(db, cancellationToken);
        var requests = await db.SampleRequests.AsNoTracking()
            .Include(item => item.Response)
            .Where(item => item.EngagementId == engagement.Id)
            .OrderByDescending(item => item.Code)
            .ToListAsync(cancellationToken);
        var findings = await db.Findings.AsNoTracking()
            .Where(item => item.EngagementId == engagement.Id)
            .OrderByDescending(item => item.Code)
            .ToListAsync(cancellationToken);
        var evidenceIds = requests.Where(item => item.Response?.EvidenceId is not null).Select(item => item.Response!.EvidenceId!.Value).ToArray();
        var evidence = await db.Evidence.AsNoTracking().Where(item => evidenceIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var choices = controls.SelectMany(controlItem => controlItem.Evidence.Select(item => new EvidenceChoiceDto(item.Id, item.Title, controlItem.Code)))
            .OrderBy(item => item.ControlCode, StringComparer.Ordinal)
            .Take(40)
            .ToArray();
        var hours = requests.Where(item => item.Response is not null)
            .Select(item => (item.Response!.RespondedAt - item.CreatedAt).TotalHours)
            .ToArray();
        var summary = new AuditEngagementDto(
            engagement.Name,
            engagement.FirmName,
            engagement.FrameworkKey,
            engagement.Scope,
            engagement.PeriodStart,
            engagement.PeriodEnd,
            engagement.FieldworkOn,
            controls.Count(item => item.Status == ControlStatus.Passing),
            controls.Count,
            requests.Count(item => item.Status == SampleStatus.Open),
            requests.Count,
            findings.Count,
            findings.Count(item => item.Severity == FindingSeverity.Exception),
            hours.Length == 0 ? null : Math.Round(hours.Average(), 1));
        return new AuditPortalPage(
            summary,
            requests.Select(item => ToRequest(item, evidence)).ToArray(),
            findings.Select(ToFinding).ToArray(),
            choices);
    }

    public async Task<Result<SampleRequestDto>> CreateRequestAsync(CreateSampleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200
            || string.IsNullOrWhiteSpace(request.Population) || request.Population.Length > 200
            || string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 100
            || request.SampleSize is < 1)
        {
            return Result<SampleRequestDto>.Fail("invalid_request");
        }

        await using var db = Open();
        var engagement = await EngagementAsync(db, cancellationToken);
        if (engagement is null)
        {
            return Result<SampleRequestDto>.Fail("not_found");
        }

        var codes = await db.SampleRequests.Select(item => item.Code).ToListAsync(cancellationToken);
        var row = new SampleRequest
        {
            Id = Guid.NewGuid(),
            EngagementId = engagement.Id,
            Code = NextCode(codes, "REQ-"),
            Title = request.Title.Trim(),
            Population = request.Population.Trim(),
            SampleSize = request.SampleSize,
            Source = request.Source.Trim(),
            DueOn = request.DueOn,
            Status = SampleStatus.Open,
            CreatedAt = time.GetUtcNow(),
        };
        db.SampleRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("audit.request.created", row.Code, cancellationToken);
        return Result<SampleRequestDto>.Ok(ToRequest(row, evidenceTitle: null));
    }

    public async Task<Result<SampleRequestDto>> RespondAsync(string code, RespondSampleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000)
        {
            return Result<SampleRequestDto>.Fail("invalid_request");
        }

        await using var db = Open();
        var row = await RequestAsync(db, code, cancellationToken);
        if (row is null)
        {
            return Result<SampleRequestDto>.Fail("not_found");
        }

        if (row.Status != SampleStatus.Open)
        {
            return Result<SampleRequestDto>.Fail("not_open");
        }

        string? evidenceTitle = null;
        if (request.EvidenceId is Guid evidenceId)
        {
            var evidence = await ScopedEvidenceAsync(db, evidenceId, cancellationToken);
            if (evidence is null)
            {
                return Result<SampleRequestDto>.Fail("not_found");
            }

            evidenceTitle = evidence.Title;
        }

        row.Status = SampleStatus.Submitted;
        var response = new SampleResponse
        {
            Id = Guid.NewGuid(),
            RequestId = row.Id,
            Body = request.Body.Trim(),
            RespondedByName = tenant.DisplayName,
            RespondedAt = time.GetUtcNow(),
            EvidenceId = request.EvidenceId,
        };
        db.SampleResponses.Add(response);
        row.Response = response;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("audit.request.responded", row.Code, cancellationToken);
        return Result<SampleRequestDto>.Ok(ToRequest(row, evidenceTitle));
    }

    public async Task<Result<SampleRequestDto>> AcceptAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await RequestAsync(db, code, cancellationToken);
        if (row is null)
        {
            return Result<SampleRequestDto>.Fail("not_found");
        }

        if (row.Status != SampleStatus.Submitted)
        {
            return Result<SampleRequestDto>.Fail("not_submitted");
        }

        row.Status = SampleStatus.Accepted;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("audit.request.accepted", row.Code, cancellationToken);
        var title = row.Response?.EvidenceId is Guid evidenceId
            ? await db.Evidence.Where(item => item.Id == evidenceId).Select(item => item.Title).FirstOrDefaultAsync(cancellationToken)
            : null;
        return Result<SampleRequestDto>.Ok(ToRequest(row, title));
    }

    public async Task<Result<FindingDto>> CreateFindingAsync(CreateFindingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200
            || string.IsNullOrWhiteSpace(request.Detail) || request.Detail.Length > 2000
            || string.IsNullOrWhiteSpace(request.RequirementCode) || request.RequirementCode.Length > 64
            || !Enum.TryParse<FindingSeverity>(request.Severity, out var severity))
        {
            return Result<FindingDto>.Fail("invalid_finding");
        }

        await using var db = Open();
        var engagement = await EngagementAsync(db, cancellationToken);
        if (engagement is null)
        {
            return Result<FindingDto>.Fail("not_found");
        }

        var codes = await db.Findings.Select(item => item.Code).ToListAsync(cancellationToken);
        var row = new Finding
        {
            Id = Guid.NewGuid(),
            EngagementId = engagement.Id,
            Code = NextCode(codes, "F-"),
            Title = request.Title.Trim(),
            Severity = severity,
            Detail = request.Detail.Trim(),
            RequirementCode = request.RequirementCode.Trim(),
        };
        db.Findings.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("audit.finding.created", row.Code, cancellationToken);
        return Result<FindingDto>.Ok(ToFinding(row));
    }

    public async Task<Result<FindingDto>> RespondToFindingAsync(string code, ManagementResponseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Response) || request.Response.Length > 2000)
        {
            return Result<FindingDto>.Fail("invalid_finding");
        }

        await using var db = Open();
        var engagement = await EngagementAsync(db, cancellationToken);
        var row = engagement is null
            ? null
            : await db.Findings.FirstOrDefaultAsync(item => item.EngagementId == engagement.Id && item.Code == code, cancellationToken);
        if (row is null)
        {
            return Result<FindingDto>.Fail("not_found");
        }

        row.ManagementResponse = request.Response.Trim();
        row.ResponseByName = tenant.DisplayName;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("audit.finding.responded", row.Code, cancellationToken);
        return Result<FindingDto>.Ok(ToFinding(row));
    }

    public async Task<Result<Guid>> StartExportAsync(CancellationToken cancellationToken)
    {
        var id = await jobs.CreateAsync(tenant.TenantId, "export", "Queued", cancellationToken);
        var keys = tenant.IsAuditor ? tenant.AuditorFrameworkKeys.ToArray() : null;
        await queue.EnqueueAsync(new ExportWork(tenant.TenantId, id, keys), cancellationToken);
        await WriteAsync("audit.package.started", id.ToString("N"), cancellationToken);
        return Result<Guid>.Ok(id);
    }

    public async Task<Result<PackageFile>> OpenPackageAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await control.Jobs.AsNoTracking().FirstOrDefaultAsync(item => item.Id == jobId && item.TenantId == tenant.TenantId, cancellationToken);
        if (job is null || job.Kind != "export")
        {
            return Result<PackageFile>.Fail("not_found");
        }

        if (job.Status != JobStatus.Succeeded)
        {
            return Result<PackageFile>.Fail("not_ready");
        }

        var key = $"{tenant.TenantId:N}/exports/{jobId:N}.zip";
        var bytes = await storage.GetAsync(key, cancellationToken);
        return Result<PackageFile>.Ok(new PackageFile(bytes, "audit-package.zip"));
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private async Task<AuditEngagement?> EngagementAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Audits.AsNoTracking().ToListAsync(cancellationToken);
        return rows
            .Where(item => !tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(item.FrameworkKey, StringComparer.Ordinal))
            .OrderBy(item => item.FieldworkOn)
            .FirstOrDefault();
    }

    private async Task<List<Control>> ScopedControlsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .Include(item => item.Evidence)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        if (!tenant.IsAuditor)
        {
            return controls;
        }

        return controls.Where(controlItem => controlItem.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal))).ToList();
    }

    private async Task<SampleRequest?> RequestAsync(TenantDbContext db, string code, CancellationToken cancellationToken)
    {
        var engagement = await EngagementAsync(db, cancellationToken);
        if (engagement is null)
        {
            return null;
        }

        return await db.SampleRequests.Include(item => item.Response)
            .FirstOrDefaultAsync(item => item.EngagementId == engagement.Id && item.Code == code, cancellationToken);
    }

    private async Task<EvidenceItem?> ScopedEvidenceAsync(TenantDbContext db, Guid evidenceId, CancellationToken cancellationToken)
    {
        var controls = await ScopedControlsAsync(db, cancellationToken);
        return controls.SelectMany(item => item.Evidence).FirstOrDefault(item => item.Id == evidenceId);
    }

    private static string NextCode(IReadOnlyList<string> codes, string prefix)
    {
        var next = 1;
        foreach (var code in codes)
        {
            if (code.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(code[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= next)
            {
                next = number + 1;
            }
        }

        return prefix + next.ToString(CultureInfo.InvariantCulture);
    }

    private static SampleRequestDto ToRequest(SampleRequest row, IReadOnlyDictionary<Guid, EvidenceItem> evidence)
    {
        var title = row.Response?.EvidenceId is Guid id && evidence.TryGetValue(id, out var item) ? item.Title : null;
        return ToRequest(row, title);
    }

    private static SampleRequestDto ToRequest(SampleRequest row, string? evidenceTitle) => new(
        row.Code,
        row.Title,
        row.Population,
        row.SampleSize,
        row.Source,
        row.DueOn,
        row.Status.ToString(),
        row.Response is null
            ? null
            : new SampleResponseDto(row.Response.Body, row.Response.RespondedByName, row.Response.RespondedAt, evidenceTitle));

    private static FindingDto ToFinding(Finding row) => new(
        row.Code,
        row.Title,
        row.Severity.ToString(),
        row.Detail,
        row.RequirementCode,
        row.ManagementResponse,
        row.ResponseByName);

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
