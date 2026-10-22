using System.Globalization;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using D2DAudit.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Governance;

public sealed class GovernancePortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IFrameworkCatalog catalog,
    EvidenceWriter evidence,
    IActivityWriter activity,
    TimeProvider time,
    IValidator<SaveRiskRequest> risks,
    IValidator<SaveSoaRequest> soa,
    IValidator<SaveVendorRequest> vendors,
    IValidator<StartCampaignRequest> campaigns) : IGovernance
{
    private const string NotApplicableCode = "A.7.5";

    public async Task<IReadOnlyList<RiskListItem>> RisksAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Risks.AsNoTracking().Include(item => item.Controls).ToListAsync(cancellationToken);
        return rows.OrderBy(item => item.Code, StringComparer.Ordinal).Select(ToRisk).ToArray();
    }

    public async Task<Result<RiskListItem>> SaveRiskAsync(SaveRiskRequest request, CancellationToken cancellationToken)
    {
        var validation = await risks.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<RiskListItem>.Fail("invalid_risk");
        }

        await using var db = Open();
        var known = await db.Controls.AsNoTracking().Select(item => item.Code).ToListAsync(cancellationToken);
        if (request.ControlCodes.Any(code => !known.Contains(code, StringComparer.Ordinal)))
        {
            return Result<RiskListItem>.Fail("invalid_control");
        }

        var code = string.IsNullOrWhiteSpace(request.Code) ? await NextRiskCodeAsync(db, cancellationToken) : request.Code.Trim();
        var row = await db.Risks.Include(item => item.Controls).FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (row is null)
        {
            row = new RiskItem { Id = Guid.NewGuid(), Code = code };
            db.Risks.Add(row);
        }

        row.Title = request.Title.Trim();
        row.Statement = request.Statement.Trim();
        row.Likelihood = request.Likelihood;
        row.Impact = request.Impact;
        row.OwnerName = request.OwnerName.Trim();
        row.OwnerEmail = request.OwnerEmail.Trim();
        row.Treatment = Enum.Parse<RiskTreatment>(request.Treatment);
        row.TreatmentPlan = request.TreatmentPlan.Trim();
        row.ReviewOn = request.ReviewOn;
        row.ResidualLikelihood = request.ResidualLikelihood;
        row.ResidualImpact = request.ResidualImpact;
        db.RiskControls.RemoveRange(row.Controls);
        row.Controls.Clear();
        foreach (var controlCode in request.ControlCodes.Distinct(StringComparer.Ordinal))
        {
            var link = new RiskControlLink { RiskId = row.Id, ControlCode = controlCode };
            row.Controls.Add(link);
            db.RiskControls.Add(link);
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("risk.saved", row.Code, cancellationToken);
        return Result<RiskListItem>.Ok(ToRisk(row));
    }

    public async Task<Result<FileExport>> ExportRisksAsync(string format, CancellationToken cancellationToken)
    {
        if (format is not ("csv" or "pdf"))
        {
            return Result<FileExport>.Fail("invalid_format");
        }

        var rows = await RisksAsync(cancellationToken);
        await WriteAsync("risk.exported", format, cancellationToken);
        return Result<FileExport>.Ok(GovernanceDocuments.Risks(format, rows));
    }

    public async Task<IReadOnlyList<PersonListItem>> PeopleAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var people = await db.People.AsNoTracking().Include(item => item.Accounts).Include(item => item.Tasks).ToListAsync(cancellationToken);
        return people.OrderBy(item => item.Name, StringComparer.Ordinal).Select(ToPersonList).ToArray();
    }

    public async Task<Result<PersonDetail>> PersonAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var person = await LoadPersonAsync(db, id, cancellationToken);
        return person is null ? Result<PersonDetail>.Fail("not_found") : Result<PersonDetail>.Ok(await ToDetailAsync(db, person, cancellationToken));
    }

    public async Task<Result<int>> SyncPeopleAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var count = await SyncAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("people.synced", count.ToString(CultureInfo.InvariantCulture), cancellationToken);
        return Result<int>.Ok(count);
    }

    public async Task<Result<PersonDetail>> SetTaskAsync(Guid id, SetTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var person = await LoadPersonAsync(db, id, cancellationToken);
        var task = person?.Tasks.FirstOrDefault(item => item.TaskCode == request.TaskCode);
        if (person is null || task is null)
        {
            return Result<PersonDetail>.Fail("not_found");
        }

        task.Done = request.Done;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("people.task", request.TaskCode, cancellationToken);
        return Result<PersonDetail>.Ok(await ToDetailAsync(db, person, cancellationToken));
    }

    public async Task<IReadOnlyList<SoaRow>> SoaAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        await EnsureSoaAsync(db, cancellationToken);
        if (tenant.IsAuditor)
        {
            await WriteAsync("soa.viewed", "iso27001", cancellationToken);
        }

        var rows = await db.Soa.AsNoTracking().ToListAsync(cancellationToken);
        return rows.OrderBy(item => AnnexOrder(item.Code)).Select(ToSoa).ToArray();
    }

    public async Task<Result<SoaRow>> SaveSoaAsync(string code, SaveSoaRequest request, CancellationToken cancellationToken)
    {
        var validation = await soa.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid || !AnnexCodes.IsControl(code))
        {
            return Result<SoaRow>.Fail("invalid_soa");
        }

        await using var db = Open();
        await EnsureSoaAsync(db, cancellationToken);
        var row = await db.Soa.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (row is null)
        {
            return Result<SoaRow>.Fail("not_found");
        }

        var implementation = Enum.Parse<SoaImplementation>(request.Implementation);
        row.Applicable = request.Applicable;
        row.Justification = request.Justification.Trim();
        row.Implementation = request.Applicable ? implementation : SoaImplementation.NotApplicable;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("soa.saved", row.Code, cancellationToken);
        return Result<SoaRow>.Ok(ToSoa(row));
    }

    public async Task<Result<FileExport>> ExportSoaAsync(string format, CancellationToken cancellationToken)
    {
        if (format is not ("csv" or "pdf"))
        {
            return Result<FileExport>.Fail("invalid_format");
        }

        var rows = await SoaAsync(cancellationToken);
        await WriteAsync("soa.exported", format, cancellationToken);
        return Result<FileExport>.Ok(GovernanceDocuments.Soa(format, rows));
    }

    public async Task<IReadOnlyList<VendorListItem>> VendorsAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Vendors.AsNoTracking().Include(item => item.Documents).ToListAsync(cancellationToken);
        return rows.OrderBy(item => item.Name, StringComparer.Ordinal).Select(ToVendor).ToArray();
    }

    public async Task<Result<VendorListItem>> SaveVendorAsync(SaveVendorRequest request, CancellationToken cancellationToken)
    {
        var validation = await vendors.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<VendorListItem>.Fail("invalid_vendor");
        }

        await using var db = Open();
        var row = request.Id is Guid id
            ? await db.Vendors.Include(item => item.Documents).FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            : null;
        if (request.Id is not null && row is null)
        {
            return Result<VendorListItem>.Fail("not_found");
        }

        row ??= new VendorRecord { Id = Guid.NewGuid() };
        if (db.Entry(row).State == EntityState.Detached)
        {
            db.Vendors.Add(row);
        }

        row.Name = request.Name.Trim();
        row.Service = request.Service.Trim();
        row.Criticality = Enum.Parse<VendorCriticality>(request.Criticality);
        row.DataAccess = request.DataAccess.Trim();
        row.ReviewState = Enum.Parse<VendorReviewState>(request.ReviewState);
        row.ReviewDueOn = request.ReviewDueOn;
        row.RiskRating = request.RiskRating;
        db.VendorDocuments.RemoveRange(row.Documents);
        row.Documents.Clear();
        foreach (var document in request.Documents)
        {
            var file = new VendorDocument
            {
                Id = Guid.NewGuid(),
                VendorId = row.Id,
                Title = document.Title.Trim(),
                Kind = document.Kind.Trim(),
                IssuedOn = document.IssuedOn,
            };
            row.Documents.Add(file);
            db.VendorDocuments.Add(file);
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("vendor.saved", row.Name, cancellationToken);
        return Result<VendorListItem>.Ok(ToVendor(row));
    }

    public async Task<IReadOnlyList<AccessCampaignListItem>> CampaignsAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.AccessCampaigns.AsNoTracking().Include(item => item.Items).ToListAsync(cancellationToken);
        return rows.OrderByDescending(item => item.CreatedAt).Select(ToCampaignList).ToArray();
    }

    public async Task<Result<AccessCampaignDetail>> CampaignAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await LoadCampaignAsync(db, id, cancellationToken);
        if (row is null)
        {
            return Result<AccessCampaignDetail>.Fail("not_found");
        }

        var hash = await EvidenceHashAsync(db, row.EvidenceId, cancellationToken);
        return Result<AccessCampaignDetail>.Ok(ToCampaign(row, hash));
    }

    public async Task<Result<AccessCampaignDetail>> StartCampaignAsync(StartCampaignRequest request, CancellationToken cancellationToken)
    {
        var validation = await campaigns.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<AccessCampaignDetail>.Fail("invalid_campaign");
        }

        await using var db = Open();
        var snapshots = await db.Resources.AsNoTracking().Where(item => item.Kind == "user").ToListAsync(cancellationToken);
        var accounts = snapshots.Select(ReadAccount).OfType<DirectoryAccount>().ToArray();
        if (accounts.Length == 0)
        {
            return Result<AccessCampaignDetail>.Fail("no_accounts");
        }

        var row = new AccessCampaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            DueOn = request.DueOn,
            State = CampaignState.Open,
            CreatedAt = time.GetUtcNow(),
        };
        foreach (var account in accounts)
        {
            var item = new AccessReviewItem
            {
                Id = Guid.NewGuid(),
                CampaignId = row.Id,
                PersonName = account.Name,
                PersonEmail = account.Email,
                Provider = account.Provider,
                AccountName = account.ExternalId,
                Decision = AccessDecision.Pending,
            };
            row.Items.Add(item);
            db.AccessItems.Add(item);
        }

        db.AccessCampaigns.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("access.started", row.Name, cancellationToken);
        return Result<AccessCampaignDetail>.Ok(ToCampaign(row, null));
    }

    public async Task<Result<AccessCampaignDetail>> DecideAsync(Guid campaignId, Guid itemId, AccessDecisionRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AccessDecision>(request.Decision, out var decision) || decision == AccessDecision.Pending)
        {
            return Result<AccessCampaignDetail>.Fail("invalid_decision");
        }

        await using var db = Open();
        var row = await LoadCampaignAsync(db, campaignId, cancellationToken);
        var item = row?.Items.FirstOrDefault(entry => entry.Id == itemId);
        if (row is null || item is null)
        {
            return Result<AccessCampaignDetail>.Fail("not_found");
        }

        if (row.State != CampaignState.Open)
        {
            return Result<AccessCampaignDetail>.Fail("campaign_closed");
        }

        item.Decision = decision;
        item.ReviewerName = tenant.DisplayName;
        item.DecidedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("access.decided", decision.ToString(), cancellationToken);
        return Result<AccessCampaignDetail>.Ok(ToCampaign(row, null));
    }

    public async Task<Result<AccessCampaignDetail>> CompleteCampaignAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await LoadCampaignAsync(db, id, cancellationToken);
        if (row is null)
        {
            return Result<AccessCampaignDetail>.Fail("not_found");
        }

        if (row.State == CampaignState.Completed)
        {
            var existingHash = await EvidenceHashAsync(db, row.EvidenceId, cancellationToken);
            return Result<AccessCampaignDetail>.Ok(ToCampaign(row, existingHash));
        }

        var rule = AccessCampaignRules.Complete(row.Items.Select(item => item.Decision).ToArray());
        if (!rule.Succeeded)
        {
            return Result<AccessCampaignDetail>.Fail(rule.Error ?? "decisions_pending");
        }

        var controlId = await db.Controls.Where(item => item.Code == "AC-07").Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await db.Controls.OrderBy(item => item.Code).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        if (controlId is null)
        {
            return Result<AccessCampaignDetail>.Fail("no_control");
        }

        var years = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstAsync(cancellationToken);
        var body = System.Text.Encoding.UTF8.GetBytes(GovernanceDocuments.ReviewCsv(row.Items.Select(ToItem).ToArray()));
        var evidenceId = Guid.NewGuid();
        var stored = new EvidenceItem
        {
            Id = evidenceId,
            ControlId = controlId.Value,
            Title = "Access review " + row.Name,
            Source = "Access review",
            ObjectKey = tenant.TenantId.ToString("N") + "/" + evidenceId.ToString("N") + ".csv",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(stored);
        await evidence.AppendAsync(db, tenant.TenantId, stored, body, "text/csv", time.GetUtcNow().AddYears(years), cancellationToken);
        row.EvidenceId = evidenceId;
        row.State = CampaignState.Completed;
        row.CompletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("access.completed", row.Name, cancellationToken);
        return Result<AccessCampaignDetail>.Ok(ToCampaign(row, stored.Sha256));
    }

    public async Task<IReadOnlyList<DueItem>> DueItemsAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var horizon = today.AddDays(30);
        var items = new List<DueItem>();
        var riskRows = await db.Risks.AsNoTracking().Where(item => item.ReviewOn <= horizon).ToListAsync(cancellationToken);
        items.AddRange(riskRows.Select(item => new DueItem("risk", item.Title, item.ReviewOn, "/risks", item.ReviewOn < today)));
        var vendorRows = await db.Vendors.AsNoTracking().Where(item => item.ReviewDueOn <= horizon).ToListAsync(cancellationToken);
        items.AddRange(vendorRows.Select(item => new DueItem("vendor", item.Name, item.ReviewDueOn, "/vendors", item.ReviewDueOn < today)));
        var people = await db.People.AsNoTracking().Where(item => item.TrainingDueOn != null && item.Training != TrainingState.Complete && item.TrainingDueOn <= horizon).ToListAsync(cancellationToken);
        items.AddRange(people.Select(item => new DueItem("training", item.Name, item.TrainingDueOn!.Value, "/people", item.TrainingDueOn < today)));
        var access = await db.AccessCampaigns.AsNoTracking().Where(item => item.State == CampaignState.Open && item.DueOn <= horizon).ToListAsync(cancellationToken);
        items.AddRange(access.Select(item => new DueItem("access", item.Name, item.DueOn, "/access-reviews", item.DueOn < today)));
        var acknowledgements = await db.Acknowledgements.AsNoTracking().Include(item => item.Version).ThenInclude(version => version!.Policy).Where(item => item.AcknowledgedAt == null && item.DueOn <= horizon).ToListAsync(cancellationToken);
        items.AddRange(acknowledgements.Select(item => new DueItem(
            "policy",
            item.Version?.Policy?.Title is string title ? title + " · " + item.PersonName : item.PersonName,
            item.DueOn,
            "/policies",
            item.DueOn < today)));
        return items.OrderBy(item => item.DueOn).ThenBy(item => item.Title, StringComparer.Ordinal).ToArray();
    }

    private async Task<int> SyncAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var snapshots = await db.Resources.AsNoTracking().Where(item => item.Kind == "user").ToListAsync(cancellationToken);
        var synced = DirectorySync.People(snapshots.Select(ReadAccount).OfType<DirectoryAccount>());
        var existing = await db.People.Include(item => item.Accounts).Include(item => item.Tasks).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var added = 0;
        foreach (var person in synced)
        {
            var row = existing.FirstOrDefault(item => string.Equals(item.Email, person.Email, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new DirectoryPerson
                {
                    Id = Guid.NewGuid(),
                    Email = person.Email,
                    Training = TrainingState.Assigned,
                    TrainingDueOn = today.AddDays(30),
                };
                AddTasks(row);
                db.People.Add(row);
                foreach (var task in row.Tasks)
                {
                    db.PersonTasks.Add(task);
                }

                added++;
            }

            row.Name = person.Name;
            row.State = person.State;
            row.Source = DirectorySync.SourceOf(person.Accounts);
            ApplyProfile(row, person.Email, today);
            db.PersonAccounts.RemoveRange(row.Accounts);
            row.Accounts.Clear();
            foreach (var account in person.Accounts)
            {
                var access = new PersonAccount
                {
                    Id = Guid.NewGuid(),
                    PersonId = row.Id,
                    Provider = account.Provider,
                    ExternalId = account.ExternalId,
                    AccountName = account.ExternalId,
                    Active = account.Active,
                };
                row.Accounts.Add(access);
                db.PersonAccounts.Add(access);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private static void ApplyProfile(DirectoryPerson row, string email, DateOnly today)
    {
        var profile = ClientSeed.Person(email);
        if (profile is null)
        {
            if (string.IsNullOrWhiteSpace(row.Department))
            {
                row.Department = "Workforce";
            }

            return;
        }

        row.Department = profile.Department;
        row.Role = profile.Role;
        row.HireOn = today.AddDays(profile.HireOffset);
        row.LeftOn = profile.LeaveOffset is int leave ? today.AddDays(leave) : null;
        if (Enum.TryParse<TrainingState>(profile.Training, out var training))
        {
            row.Training = training;
        }

        row.TrainingDueOn = today.AddDays(profile.TrainingDueOffset);
    }

    private async Task EnsureSoaAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Soa.AnyAsync(cancellationToken))
        {
            return;
        }

        var framework = catalog.Frameworks.FirstOrDefault(item => item.Key == "iso27001");
        if (framework is null)
        {
            return;
        }

        var leaves = new List<RequirementNode>();
        Collect(framework.Requirements, leaves);
        var mapped = await MappedStatusAsync(db, cancellationToken);
        foreach (var leaf in leaves.Where(item => AnnexCodes.IsControl(item.Code)))
        {
            var excluded = leaf.Code == NotApplicableCode;
            db.Soa.Add(new SoaEntry
            {
                Id = Guid.NewGuid(),
                Code = leaf.Code,
                Title = leaf.Title,
                Applicable = !excluded,
                Justification = excluded
                    ? "The example company does not run its own data center. The office building is covered by the landlord."
                    : "This control applies to the way the company handles information.",
                Implementation = excluded
                    ? SoaImplementation.NotApplicable
                    : mapped.GetValueOrDefault(leaf.Code, SoaImplementation.NotStarted),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, SoaImplementation>> MappedStatusAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking().Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement).ToListAsync(cancellationToken);
        var result = new Dictionary<string, SoaImplementation>(StringComparer.Ordinal);
        foreach (var controlItem in controls)
        {
            foreach (var code in controlItem.Mappings.Select(mapping => mapping.Requirement?.Code).OfType<string>().Where(AnnexCodes.IsControl))
            {
                var next = controlItem.Status == ControlStatus.Passing ? SoaImplementation.Implemented : SoaImplementation.InProgress;
                if (!result.TryGetValue(code, out var current) || current == SoaImplementation.Implemented)
                {
                    result[code] = next;
                }
            }
        }

        return result;
    }

    private static void Collect(IEnumerable<RequirementNode> nodes, List<RequirementNode> leaves)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count == 0)
            {
                leaves.Add(node);
            }
            else
            {
                Collect(node.Children, leaves);
            }
        }
    }

    private static void AddTasks(DirectoryPerson person)
    {
        foreach (var code in ChecklistTasks.Onboarding())
        {
            person.Tasks.Add(new PersonTask { Id = Guid.NewGuid(), PersonId = person.Id, Kind = ChecklistKind.Onboarding, TaskCode = code });
        }

        foreach (var code in ChecklistTasks.Offboarding())
        {
            person.Tasks.Add(new PersonTask { Id = Guid.NewGuid(), PersonId = person.Id, Kind = ChecklistKind.Offboarding, TaskCode = code });
        }
    }

    private static DirectoryAccount? ReadAccount(ResourceSnapshot snapshot)
    {
        Dictionary<string, string>? attributes;
        try
        {
            attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.AttributesJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (attributes is null || !attributes.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        attributes.TryGetValue("offboarded", out var offboarded);
        attributes.TryGetValue("status", out var status);
        return new DirectoryAccount(snapshot.Provider, snapshot.ExternalId, snapshot.Name, email, offboarded == "true", status == "active");
    }

    private static (int Major, int Minor) AnnexOrder(string code)
    {
        var parts = code.Split('.');
        var major = parts.Length > 1 && int.TryParse(parts[1], out var left) ? left : 0;
        var minor = parts.Length > 2 && int.TryParse(parts[2], out var right) ? right : 0;
        return (major, minor);
    }

    private static async Task<string> NextRiskCodeAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var codes = await db.Risks.Select(item => item.Code).ToListAsync(cancellationToken);
        var number = codes.Select(code => code.StartsWith("R-", StringComparison.Ordinal) && int.TryParse(code[2..], out var value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return "R-" + number.ToString("00", CultureInfo.InvariantCulture);
    }

    private static async Task<DirectoryPerson?> LoadPersonAsync(TenantDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.People.Include(item => item.Accounts).Include(item => item.Tasks).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static async Task<AccessCampaign?> LoadCampaignAsync(TenantDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.AccessCampaigns.Include(item => item.Items).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static async Task<string?> EvidenceHashAsync(TenantDbContext db, Guid? evidenceId, CancellationToken cancellationToken)
    {
        if (evidenceId is not Guid id)
        {
            return null;
        }

        return await db.Evidence.AsNoTracking().Where(item => item.Id == id).Select(item => item.Sha256).FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<PersonDetail> ToDetailAsync(TenantDbContext db, DirectoryPerson person, CancellationToken cancellationToken)
    {
        var acknowledgements = await db.Acknowledgements.AsNoTracking()
            .Include(item => item.Version).ThenInclude(version => version!.Policy)
            .Where(item => item.PersonEmail == person.Email)
            .ToListAsync(cancellationToken);
        return new PersonDetail(
            person.Id,
            person.Name,
            person.Email,
            person.Department,
            person.Source.ToString(),
            person.State.ToString(),
            person.Training.ToString(),
            person.TrainingDueOn,
            LeaverAccess.Flagged(person.State, person.Accounts.Select(account => account.Active)),
            person.Accounts.OrderBy(account => account.Provider, StringComparer.Ordinal).Select(account => new PersonAccountDto(account.Provider, account.AccountName, account.Active)).ToArray(),
            person.Tasks.OrderBy(task => task.Kind).ThenBy(task => task.TaskCode, StringComparer.Ordinal).Select(task => new PersonTaskDto(task.Kind.ToString(), task.TaskCode, task.Done)).ToArray(),
            acknowledgements.Select(item => new PersonAcknowledgementDto(item.Version?.Policy?.Code ?? "", item.Version?.Policy?.Title ?? "", item.DueOn, item.AcknowledgedAt)).ToArray(),
            person.Role,
            person.HireOn,
            person.LeftOn);
    }

    private static RiskListItem ToRisk(RiskItem row)
    {
        var score = RiskScoring.Score(row.Likelihood, row.Impact);
        var residual = RiskScoring.Score(row.ResidualLikelihood, row.ResidualImpact);
        return new RiskListItem(
            row.Code,
            row.Title,
            row.Statement,
            row.Likelihood,
            row.Impact,
            score,
            RiskScoring.Band(score),
            row.OwnerName,
            row.OwnerEmail,
            row.Treatment.ToString(),
            row.TreatmentPlan,
            row.ReviewOn,
            row.ResidualLikelihood,
            row.ResidualImpact,
            residual,
            RiskScoring.Band(residual),
            row.Controls.Select(link => link.ControlCode).Order(StringComparer.Ordinal).ToArray());
    }

    private static PersonListItem ToPersonList(DirectoryPerson person) => new(
        person.Id,
        person.Name,
        person.Email,
        person.Department,
        person.Source.ToString(),
        person.State.ToString(),
        person.Training.ToString(),
        person.TrainingDueOn,
        LeaverAccess.Flagged(person.State, person.Accounts.Select(account => account.Active)),
        person.Tasks.Count(task => !task.Done && (person.State == PersonState.Offboarded ? task.Kind == ChecklistKind.Offboarding : task.Kind == ChecklistKind.Onboarding)),
        person.Role,
        person.HireOn,
        person.LeftOn);

    private static SoaRow ToSoa(SoaEntry row) => new(row.Code, row.Title, row.Applicable, row.Justification, row.Implementation.ToString());

    private static VendorListItem ToVendor(VendorRecord row) => new(
        row.Id,
        row.Name,
        row.Service,
        row.Criticality.ToString(),
        row.DataAccess,
        row.ReviewState.ToString(),
        row.ReviewDueOn,
        row.RiskRating,
        row.Documents.Select(document => new VendorDocumentDto(document.Title, document.Kind, document.IssuedOn)).ToArray());

    private static AccessCampaignListItem ToCampaignList(AccessCampaign row) =>
        new(row.Id, row.Name, row.DueOn, row.State.ToString(), row.Items.Count(item => item.Decision == AccessDecision.Pending), row.Items.Count);

    private static AccessCampaignDetail ToCampaign(AccessCampaign row, string? hash) => new(
        row.Id,
        row.Name,
        row.DueOn,
        row.State.ToString(),
        row.CompletedAt,
        row.EvidenceId,
        hash,
        row.Items.OrderBy(item => item.PersonName, StringComparer.Ordinal).ThenBy(item => item.Provider, StringComparer.Ordinal).Select(ToItem).ToArray());

    private static AccessItemDto ToItem(AccessReviewItem item) =>
        new(item.Id, item.PersonName, item.PersonEmail, item.Provider, item.AccountName, item.Decision.ToString());

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
