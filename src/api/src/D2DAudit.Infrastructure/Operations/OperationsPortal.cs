using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Operations;

public sealed class OperationsPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    IAutomationDispatcher automation,
    TimeProvider time) : IOperations
{
    private static readonly string[] Triggers = ["TestFailed", "ControlNotReady", "RiskCreated", "ReviewDue"];

    private static readonly string[] Actions = ["Jira", "Slack", "Teams", "Email"];

    public async Task<IReadOnlyList<ComputerItem>> ComputersAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Computers.AsNoTracking().OrderBy(item => item.OwnerName).ThenBy(item => item.Serial).ToListAsync(cancellationToken);
        return rows.Select(ToComputer).ToArray();
    }

    public async Task<Result<ComputerItem>> SaveComputerAsync(SaveComputerRequest request, CancellationToken cancellationToken)
    {
        var serial = (request.Serial ?? "").Trim();
        var system = (request.OperatingSystem ?? "").Trim();
        var owner = (request.OwnerName ?? "").Trim();
        var email = (request.OwnerEmail ?? "").Trim();
        var source = request.Source == "Mdm" ? "Mdm" : "Manual";
        if (serial.Length is 0 or > 64 || system.Length is 0 or > 80 || owner.Length is 0 or > 200 || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result<ComputerItem>.Fail("invalid_computer");
        }

        await using var db = Open();
        ComputerRecord row;
        if (request.Id is Guid id)
        {
            var existing = await db.Computers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (existing is null)
            {
                return Result<ComputerItem>.Fail("not_found");
            }

            row = existing;
        }
        else
        {
            row = new ComputerRecord { Id = Guid.NewGuid(), LastCheckIn = time.GetUtcNow() };
            db.Computers.Add(row);
        }

        var taken = await db.Computers.AnyAsync(item => item.Serial == serial && item.Id != row.Id, cancellationToken);
        if (taken)
        {
            return Result<ComputerItem>.Fail("invalid_computer");
        }

        row.Serial = serial;
        row.OperatingSystem = system;
        row.OwnerName = owner;
        row.OwnerEmail = email;
        row.Encrypted = request.Encrypted;
        row.ScreenLock = request.ScreenLock;
        row.Antivirus = request.Antivirus;
        row.PasswordManager = request.PasswordManager;
        row.AutoUpdate = request.AutoUpdate;
        row.Source = source;
        if (request.Id is null)
        {
            row.LastCheckIn = time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "computer.saved", "ok", serial, null, cancellationToken);
        return Result<ComputerItem>.Ok(ToComputer(row));
    }

    public async Task<Result<ComputerItem>> UploadScreenshotAsync(Guid id, ScreenshotRequest request, CancellationToken cancellationToken)
    {
        var name = (request.FileName ?? "").Trim();
        if (name.Length is 0 or > 200 || string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return Result<ComputerItem>.Fail("invalid_evidence");
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return Result<ComputerItem>.Fail("invalid_evidence");
        }

        if (body.Length is 0 or > 750_000)
        {
            return Result<ComputerItem>.Fail("invalid_evidence");
        }

        await using var db = Open();
        var row = await db.Computers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<ComputerItem>.Fail("not_found");
        }

        var stored = await StoreEvidenceAsync(db, "EP-03", "Computer " + row.Serial + " screenshot", "Manual", body, cancellationToken);
        if (stored is null)
        {
            return Result<ComputerItem>.Fail("not_found");
        }

        row.ScreenshotName = name;
        row.EvidenceId = stored.Value;
        row.LastCheckIn = time.GetUtcNow();
        row.Source = row.Source == "Mdm" ? "Mdm" : "Manual";
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "computer.screenshot", "ok", row.Serial, null, cancellationToken);
        return Result<ComputerItem>.Ok(ToComputer(row));
    }

    public async Task<VulnerabilityBoard> VulnerabilitiesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await PolicyAsync(db, cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var rows = await db.Vulnerabilities.AsNoTracking().OrderBy(item => item.AssetName).ThenBy(item => item.Cve).ToListAsync(cancellationToken);
        var items = rows.Select(row => ToVulnerability(row, today)).ToArray();
        var open = items.Where(item => !item.Ignored).ToArray();
        var assets = open.GroupBy(item => item.AssetName, StringComparer.Ordinal)
            .Select(group => new VulnerabilityAssetGroup(
                group.Key,
                group.Count(),
                group.Count(item => item.Severity == "Critical"),
                group.Count(item => item.Severity == "High"),
                group.ToArray()))
            .OrderByDescending(group => group.Critical)
            .ThenBy(group => group.AssetName, StringComparer.Ordinal)
            .ToArray();
        var history = await db.SlaEvents.AsNoTracking().OrderByDescending(item => item.At).Take(80).ToListAsync(cancellationToken);
        return new VulnerabilityBoard(
            new VulnerabilitySlaDto(policy.CriticalDays, policy.HighDays, policy.MediumDays, policy.LowDays),
            open,
            assets,
            items.Where(item => item.Ignored).ToArray(),
            history.Select(item => new SlaHistoryItem(item.Cve, item.AssetName, item.EventKind, item.DueOn, item.Detail, item.At)).ToArray());
    }

    public async Task<Result<VulnerabilityItem>> IgnoreVulnerabilityAsync(Guid id, IgnoreVulnerabilityRequest request, CancellationToken cancellationToken)
    {
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length is 0 or > 500)
        {
            return Result<VulnerabilityItem>.Fail("invalid_vulnerability");
        }

        await using var db = Open();
        var row = await db.Vulnerabilities.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<VulnerabilityItem>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        row.Ignored = true;
        row.IgnoreReason = reason;
        row.IgnoredAt = now;
        db.SlaEvents.Add(new SlaEvent
        {
            Id = Guid.NewGuid(),
            Cve = row.Cve,
            AssetName = row.AssetName,
            EventKind = "Ignored",
            DueOn = row.SlaDue,
            Detail = reason,
            At = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "vulnerability.ignored", "ok", row.Cve, null, cancellationToken);
        return Result<VulnerabilityItem>.Ok(ToVulnerability(row, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    public async Task<Result<VulnerabilitySlaDto>> SaveSlaAsync(VulnerabilitySlaDto request, CancellationToken cancellationToken)
    {
        if (request.CriticalDays is < 1 or > 365 || request.HighDays is < 1 or > 365 || request.MediumDays is < 1 or > 365 || request.LowDays is < 1 or > 365)
        {
            return Result<VulnerabilitySlaDto>.Fail("invalid_sla");
        }

        await using var db = Open();
        var policy = await PolicyAsync(db, cancellationToken);
        policy.CriticalDays = request.CriticalDays;
        policy.HighDays = request.HighDays;
        policy.MediumDays = request.MediumDays;
        policy.LowDays = request.LowDays;
        var now = time.GetUtcNow();
        var open = await db.Vulnerabilities.Where(item => !item.Ignored).ToListAsync(cancellationToken);
        foreach (var row in open)
        {
            var due = VulnerabilityCalendar.Due(row.FirstSeen, row.Severity, policy.CriticalDays, policy.HighDays, policy.MediumDays, policy.LowDays);
            if (due == row.SlaDue)
            {
                continue;
            }

            row.SlaDue = due;
            db.SlaEvents.Add(new SlaEvent
            {
                Id = Guid.NewGuid(),
                Cve = row.Cve,
                AssetName = row.AssetName,
                EventKind = "DueChanged",
                DueOn = due,
                Detail = row.Severity + " window is " + VulnerabilityCalendar.Days(row.Severity, policy.CriticalDays, policy.HighDays, policy.MediumDays, policy.LowDays) + " days",
                At = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "vulnerability.sla", "ok", null, null, cancellationToken);
        return Result<VulnerabilitySlaDto>.Ok(new VulnerabilitySlaDto(policy.CriticalDays, policy.HighDays, policy.MediumDays, policy.LowDays));
    }

    public async Task<TrustAdminPage> TrustAdminAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var profile = await ProfileAsync(db, cancellationToken);
        return await AdminPageAsync(db, profile, cancellationToken);
    }

    public async Task<Result<TrustAdminPage>> SaveTrustAsync(SaveTrustRequest request, CancellationToken cancellationToken)
    {
        var headline = (request.Headline ?? "").Trim();
        var summary = (request.Summary ?? "").Trim();
        var email = (request.ContactEmail ?? "").Trim();
        if (headline.Length is 0 or > 200 || summary.Length is 0 or > 2000 || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result<TrustAdminPage>.Fail("invalid_trust");
        }

        await using var db = Open();
        var profile = await ProfileAsync(db, cancellationToken);
        profile.Published = request.Published;
        profile.Headline = headline;
        profile.Summary = summary;
        profile.ContactEmail = email;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "trust.saved", "ok", null, null, cancellationToken);
        return Result<TrustAdminPage>.Ok(await AdminPageAsync(db, profile, cancellationToken));
    }

    public async Task<Result<TrustDocumentDto>> SaveTrustDocumentAsync(SaveTrustDocumentRequest request, CancellationToken cancellationToken)
    {
        var title = (request.Title ?? "").Trim();
        var summary = (request.Summary ?? "").Trim();
        var access = request.Access is "Nda" or "Public" ? request.Access : "";
        var file = (request.FileName ?? "").Trim();
        if (title.Length is 0 or > 200 || summary.Length is 0 or > 1000 || access.Length == 0 || file.Length is 0 or > 200)
        {
            return Result<TrustDocumentDto>.Fail("invalid_trust");
        }

        await using var db = Open();
        TrustDocument row;
        if (request.Id is Guid id)
        {
            var existing = await db.TrustDocuments.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (existing is null)
            {
                return Result<TrustDocumentDto>.Fail("not_found");
            }

            row = existing;
        }
        else
        {
            row = new TrustDocument { Id = Guid.NewGuid() };
            db.TrustDocuments.Add(row);
        }

        row.Title = title;
        row.Summary = summary;
        row.Access = access;
        row.FileName = file;
        row.UpdatedOn = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "trust.document", "ok", title, null, cancellationToken);
        return Result<TrustDocumentDto>.Ok(ToDocument(row));
    }

    public async Task<Result<TrustRequestDto>> DecideTrustAsync(Guid id, DecideTrustRequest request, CancellationToken cancellationToken)
    {
        var status = request.Status == "Approved" || request.Status == "Denied" ? request.Status : "";
        if (status.Length == 0)
        {
            return Result<TrustRequestDto>.Fail("invalid_trust");
        }

        await using var db = Open();
        var row = await db.TrustRequests.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<TrustRequestDto>.Fail("not_found");
        }

        row.Status = status;
        row.DecidedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "trust.request", "ok", status, null, cancellationToken);
        var title = row.DocumentId is Guid documentId
            ? await db.TrustDocuments.Where(item => item.Id == documentId).Select(item => item.Title).FirstOrDefaultAsync(cancellationToken) ?? ""
            : "";
        return Result<TrustRequestDto>.Ok(ToRequest(row, title));
    }

    public async Task<TrustPublicPage?> TrustPublicAsync(string slug, CancellationToken cancellationToken)
    {
        var match = await OpenBySlugAsync(slug, cancellationToken);
        if (match is null)
        {
            return null;
        }

        await using var db = match.Value.Db;
        var profile = await db.TrustProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || !profile.Published)
        {
            return null;
        }

        var documents = await db.TrustDocuments.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken);
        var frameworks = await FrameworkSummaryAsync(db, cancellationToken);
        return new TrustPublicPage(
            match.Value.Tenant.Slug,
            match.Value.Tenant.Name,
            profile.Headline,
            profile.Summary,
            profile.ContactEmail,
            frameworks,
            documents.Select(item => new TrustDocumentDto(item.Id, item.Title, item.Summary, item.Access, item.Access == "Public" ? item.FileName : "", item.UpdatedOn)).ToArray());
    }

    public async Task<Result<TrustRequestDto>> RequestTrustAccessAsync(string slug, TrustAccessRequestBody request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        var email = (request.Email ?? "").Trim();
        var company = (request.Company ?? "").Trim();
        var purpose = (request.Purpose ?? "").Trim();
        if (name.Length is 0 or > 200 || !email.Contains('@', StringComparison.Ordinal) || email.Length > 320 || company.Length is 0 or > 200 || purpose.Length is 0 or > 1000)
        {
            return Result<TrustRequestDto>.Fail("invalid_trust");
        }

        var match = await OpenBySlugAsync(slug, cancellationToken);
        if (match is null)
        {
            return Result<TrustRequestDto>.Fail("not_found");
        }

        await using var db = match.Value.Db;
        var profile = await db.TrustProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || !profile.Published)
        {
            return Result<TrustRequestDto>.Fail("not_found");
        }

        string title = "";
        if (request.DocumentId is Guid documentId)
        {
            var document = await db.TrustDocuments.FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
            if (document is null)
            {
                return Result<TrustRequestDto>.Fail("not_found");
            }

            title = document.Title;
        }

        var row = new TrustAccessRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = request.DocumentId,
            RequesterName = name,
            RequesterEmail = email,
            Company = company,
            Purpose = purpose,
            Status = "Pending",
            RequestedAt = time.GetUtcNow(),
        };
        db.TrustRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Result<TrustRequestDto>.Ok(ToRequest(row, title));
    }

    public async Task<IReadOnlyList<AutomationRuleDto>> RulesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.AutomationRules.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return rows.Select(ToRule).ToArray();
    }

    public async Task<IReadOnlyList<AutomationDeliveryDto>> DeliveriesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.AutomationDeliveries.AsNoTracking().OrderByDescending(item => item.At).Take(40).ToListAsync(cancellationToken);
        return rows.Select(ToDelivery).ToArray();
    }

    public async Task<Result<AutomationRuleDto>> SaveRuleAsync(SaveAutomationRuleRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        var target = (request.Target ?? "").Trim();
        if (name.Length is 0 or > 200 || target.Length is 0 or > 200 || !Triggers.Contains(request.Trigger) || !Actions.Contains(request.Action))
        {
            return Result<AutomationRuleDto>.Fail("invalid_rule");
        }

        await using var db = Open();
        AutomationRule row;
        if (request.Id is Guid id)
        {
            var existing = await db.AutomationRules.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (existing is null)
            {
                return Result<AutomationRuleDto>.Fail("not_found");
            }

            row = existing;
        }
        else
        {
            row = new AutomationRule { Id = Guid.NewGuid() };
            db.AutomationRules.Add(row);
        }

        row.Name = name;
        row.Trigger = request.Trigger;
        row.Action = request.Action;
        row.Target = target;
        row.Enabled = request.Enabled;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "automation.saved", "ok", request.Trigger, null, cancellationToken);
        return Result<AutomationRuleDto>.Ok(ToRule(row));
    }

    public async Task<Result<IReadOnlyList<AutomationDeliveryDto>>> RunDueAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var horizon = today.AddDays(14);
        var risks = await db.Risks.AsNoTracking().Where(item => item.ReviewOn <= horizon).Select(item => item.Title).ToListAsync(cancellationToken);
        var vendors = await db.Vendors.AsNoTracking().Where(item => item.ReviewDueOn <= horizon).Select(item => item.Name).ToListAsync(cancellationToken);
        var names = risks.Concat(vendors).Take(12).ToArray();
        if (names.Length == 0)
        {
            return Result<IReadOnlyList<AutomationDeliveryDto>>.Ok(await DeliveriesAsync(cancellationToken));
        }

        await automation.DispatchAsync("ReviewDue", "Reviews due", string.Join(", ", names), cancellationToken);
        return Result<IReadOnlyList<AutomationDeliveryDto>>.Ok(await DeliveriesAsync(cancellationToken));
    }

    public async Task<Result<CreatedControl>> CreateControlAsync(CreateControlRequest request, CancellationToken cancellationToken)
    {
        var parsed = ControlCsv.Parse(
            "code,title,description,frequency,ownerName,ownerEmail,framework,requirement,coverage\n"
            + string.Join(',', new[]
            {
                request.Code, request.Title, request.Description, request.Frequency, request.OwnerName, request.OwnerEmail, request.Framework, request.Requirement, request.Coverage,
            }.Select(Quote)));
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Result<CreatedControl>.Fail("invalid_control");
        }

        var count = await ImportRowsAsync(parsed.Value, cancellationToken);
        return count.IsSuccess
            ? Result<CreatedControl>.Ok(new CreatedControl(parsed.Value[0].Code.Trim(), parsed.Value[0].Title.Trim(), true))
            : Result<CreatedControl>.Fail(count.Error ?? "invalid_control");
    }

    public async Task<Result<int>> ImportControlsAsync(ImportControlsRequest request, CancellationToken cancellationToken)
    {
        var parsed = ControlCsv.Parse(request.Csv);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Result<int>.Fail(parsed.Error ?? "invalid_import");
        }

        return await ImportRowsAsync(parsed.Value, cancellationToken);
    }

    private async Task<Result<int>> ImportRowsAsync(IReadOnlyList<ControlImportRow> rows, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var requirements = await db.Requirements.Include(item => item.Framework).ToListAsync(cancellationToken);
        var byKey = requirements.ToDictionary(item => (item.Framework!.Key, item.Code), item => item);
        var controls = await db.Controls.Include(item => item.Mappings).ToListAsync(cancellationToken);
        var byCode = controls.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var row in rows)
        {
            if (!byKey.TryGetValue((row.Framework.Trim(), row.Requirement.Trim()), out var requirement))
            {
                return Result<int>.Fail("invalid_control");
            }

            if (row.OwnerEmail.Length > 0 && !row.OwnerEmail.Contains('@', StringComparison.Ordinal))
            {
                return Result<int>.Fail("invalid_control");
            }

            var coverage = string.Equals(row.Coverage, "partial", StringComparison.OrdinalIgnoreCase) ? MappingCoverage.Partial : MappingCoverage.Full;
            if (!byCode.TryGetValue(row.Code.Trim(), out var controlItem))
            {
                controlItem = new Control
                {
                    Id = Guid.NewGuid(),
                    Code = row.Code.Trim(),
                    Title = row.Title.Trim(),
                    Description = row.Description.Trim(),
                    Frequency = string.IsNullOrWhiteSpace(row.Frequency) ? "Annual" : row.Frequency.Trim(),
                    OwnerName = row.OwnerName.Trim(),
                    OwnerEmail = row.OwnerEmail.Trim(),
                    Status = ControlStatus.Manual,
                    Custom = true,
                };
                db.Controls.Add(controlItem);
                byCode[controlItem.Code] = controlItem;
                added++;
            }

            if (controlItem.Mappings.All(item => item.RequirementId != requirement.Id))
            {
                var map = new ControlRequirementMap
                {
                    ControlId = controlItem.Id,
                    RequirementId = requirement.Id,
                    Coverage = coverage,
                };
                controlItem.Mappings.Add(map);
                db.ControlMappings.Add(map);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "control.imported", "ok", added.ToString(), null, cancellationToken);
        return Result<int>.Ok(rows.Count);
    }

    private async Task<Guid?> StoreEvidenceAsync(TenantDbContext db, string controlCode, string title, string source, byte[] body, CancellationToken cancellationToken)
    {
        var controlItem = await db.Controls.FirstOrDefaultAsync(item => item.Code == controlCode, cancellationToken)
            ?? await db.Controls.OrderBy(item => item.Code).FirstOrDefaultAsync(cancellationToken);
        if (controlItem is null)
        {
            return null;
        }

        var retention = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstAsync(cancellationToken);
        var id = Guid.NewGuid();
        var item = new EvidenceItem
        {
            Id = id,
            ControlId = controlItem.Id,
            Title = title,
            Source = source,
            ObjectKey = tenant.TenantId.ToString("N") + "/" + id.ToString("N") + ".bin",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(item);
        await evidence.AppendAsync(db, tenant.TenantId, item, body, "application/octet-stream", time.GetUtcNow().AddYears(retention), cancellationToken);
        return id;
    }

    private async Task<TrustAdminPage> AdminPageAsync(TenantDbContext db, TrustProfile profile, CancellationToken cancellationToken)
    {
        var slug = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.Slug).FirstAsync(cancellationToken);
        var documents = await db.TrustDocuments.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken);
        var requests = await db.TrustRequests.AsNoTracking().OrderByDescending(item => item.RequestedAt).ToListAsync(cancellationToken);
        var titles = documents.ToDictionary(item => item.Id, item => item.Title);
        return new TrustAdminPage(
            slug,
            profile.Published,
            profile.Headline,
            profile.Summary,
            profile.ContactEmail,
            documents.Select(ToDocument).ToArray(),
            requests.Select(item => ToRequest(item, item.DocumentId is Guid id && titles.TryGetValue(id, out var title) ? title : "")).ToArray());
    }

    private static async Task<IReadOnlyList<TrustFrameworkDto>> FrameworkSummaryAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .ToListAsync(cancellationToken);
        return controls.SelectMany(item => item.Mappings.Select(mapping => mapping.Requirement?.Framework).Where(framework => framework is not null).Select(framework => (framework!, item)))
            .GroupBy(pair => pair.Item1.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var distinct = group.Select(pair => pair.item).DistinctBy(item => item.Id).ToArray();
                return new TrustFrameworkDto(group.Key, group.First().Item1.Name, distinct.Length, distinct.Count(item => item.Status == ControlStatus.Passing));
            })
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<(Tenant Tenant, TenantDbContext Db)?> OpenBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var key = (slug ?? "").Trim().ToLowerInvariant();
        var match = await control.Tenants.AsNoTracking().FirstOrDefaultAsync(item => item.Slug == key && item.Status == TenantStatus.Active, cancellationToken);
        if (match is null || string.IsNullOrWhiteSpace(match.DatabaseName))
        {
            return null;
        }

        return (match, databases.Create(match.DatabaseName));
    }

    private static async Task<VulnerabilityPolicy> PolicyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var policy = await db.VulnerabilityPolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is not null)
        {
            return policy;
        }

        policy = new VulnerabilityPolicy { Id = Guid.NewGuid() };
        db.VulnerabilityPolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private async Task<TrustProfile> ProfileAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var profile = await db.TrustProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        var name = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.Name).FirstAsync(cancellationToken);
        profile = new TrustProfile
        {
            Id = Guid.NewGuid(),
            Published = false,
            Headline = name + " trust center",
            Summary = "Security and compliance documents published by " + name + ".",
            ContactEmail = "security@example.com",
        };
        db.TrustProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }

    private static ComputerItem ToComputer(ComputerRecord row) => new(
        row.Id,
        row.Serial,
        row.OperatingSystem,
        row.OwnerName,
        row.OwnerEmail,
        row.Encrypted,
        row.ScreenLock,
        row.Antivirus,
        row.PasswordManager,
        row.AutoUpdate,
        row.LastCheckIn,
        row.Source,
        row.ScreenshotName,
        row.EvidenceId);

    private static VulnerabilityItem ToVulnerability(VulnerabilityRecord row, DateOnly today) => new(
        row.Id,
        row.Cve,
        row.Title,
        row.Severity,
        row.AssetName,
        row.Source,
        row.FirstSeen,
        row.LastSeen,
        row.SlaDue,
        row.FixAvailable,
        row.Ignored,
        row.IgnoreReason,
        !row.Ignored && row.SlaDue < today);

    private static TrustDocumentDto ToDocument(TrustDocument row) => new(row.Id, row.Title, row.Summary, row.Access, row.FileName, row.UpdatedOn);

    private static TrustRequestDto ToRequest(TrustAccessRequest row, string title) => new(
        row.Id,
        row.DocumentId,
        title,
        row.RequesterName,
        row.RequesterEmail,
        row.Company,
        row.Purpose,
        row.Status,
        row.RequestedAt,
        row.DecidedAt);

    private static AutomationRuleDto ToRule(AutomationRule row) => new(row.Id, row.Name, row.Trigger, row.Action, row.Target, row.Enabled);

    private static AutomationDeliveryDto ToDelivery(AutomationDelivery row) => new(row.Id, row.Trigger, row.Action, row.Target, row.Summary, row.ExternalId, row.At);

    private static string Quote(string? value)
    {
        var text = value ?? "";
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
