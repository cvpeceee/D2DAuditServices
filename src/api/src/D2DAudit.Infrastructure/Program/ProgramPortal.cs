using System.Globalization;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Program;

public sealed class ProgramPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    TimeProvider time,
    PasswordHasher<UserAccount> passwords) : IProgram
{
    private static readonly string[] AssetTypes = ["Server", "Database", "Bucket", "Repository", "Laptop", "Saas"];
    private static readonly string[] Classifications = ["Public", "Internal", "Confidential", "Restricted"];
    private static readonly string[] TaskKinds = ["Control", "Test", "Risk", "Vendor", "Policy"];
    private static readonly string[] TaskStatuses = ["Open", "InProgress", "Done"];
    private static readonly string[] Recurrences = ["None", "Weekly", "Monthly", "Quarterly", "Annual"];
    private static readonly string[] CardKinds = ["Policy", "Training", "Mfa", "Device", "BackgroundCheck"];

    public async Task<IReadOnlyList<MeCard>> CardsAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var person = await FindPersonAsync(db, tenant.Email, cancellationToken);
        if (person is null)
        {
            return CardKinds.Select(kind => new MeCard(kind, CardTitle(kind), "Unassigned", null, "none", null, null)).ToArray();
        }

        var acknowledgements = await AcknowledgementsAsync(db, person.Email, cancellationToken);
        var openAcks = acknowledgements.Where(item => item.AcknowledgedAt is null).ToArray();
        var annual = await CompletionAsync(db, TrainingContent.AnnualCode, person.Email, cancellationToken);
        var deviceReady = person.DeviceStatus is "Reporting" or "Screenshot";
        var cards = new List<MeCard>
        {
            Card("Policy", AckStatus(openAcks, Today()), openAcks.Length == 0 ? null : openAcks.Min(item => item.DueOn), openAcks.Length == 0 ? "done" : "acknowledge", null),
            Card("Training", TrainingStatus(person), person.TrainingDueOn, person.Training == TrainingState.Complete ? "done" : "open", annual.Evidence),
            Card("Mfa", person.MfaEnrolled ? "Complete" : "Due", person.HireOn?.AddDays(14), person.MfaEnrolled ? "done" : "attest", null),
            Card("Device", deviceReady ? "Complete" : "Due", Today().AddDays(7), deviceReady ? "done" : "upload", null),
            Card("BackgroundCheck", BackgroundLabel(person.BackgroundStatus), person.BackgroundOn, person.BackgroundStatus == "Clear" ? "done" : "record", null),
        };
        return await WithHashesAsync(db, cards, cancellationToken);
    }

    public async Task<Result<MeCard>> CompleteCardAsync(CompleteCardRequest request, CancellationToken cancellationToken)
    {
        if (!CardKinds.Contains(request.Kind))
        {
            return Result<MeCard>.Fail("invalid_card");
        }

        await using var db = Open();
        var person = await FindPersonAsync(db, tenant.Email, cancellationToken);
        if (person is null)
        {
            return Result<MeCard>.Fail("not_found");
        }

        if (!tenant.CanWrite && !person.Email.Equals(tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<MeCard>.Fail("forbidden");
        }

        var today = Today();
        Guid? evidenceId = null;
        if (request.Kind == "Policy")
        {
            var rows = await db.Acknowledgements.Include(item => item.Version).Where(item => item.PersonEmail == person.Email && item.AcknowledgedAt == null).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.AcknowledgedAt = time.GetUtcNow();
            }

            Mark(person, "policies");
            evidenceId = await StoreAsync(db, "HR-01", "Policy acknowledgement " + person.Name, "Employee portal", person.Name + " acknowledged the assigned policies on " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".", cancellationToken);
        }
        else if (request.Kind == "Training")
        {
            return Result<MeCard>.Fail("use_module");
        }
        else if (request.Kind == "Mfa")
        {
            person.MfaEnrolled = true;
            Mark(person, "mfa");
            evidenceId = await StoreAsync(db, "HR-06", "MFA attestation " + person.Name, "Employee portal", person.Name + " confirmed a second factor is enrolled.", cancellationToken);
            await RefreshMfaTestAsync(db, cancellationToken);
        }
        else if (request.Kind == "Device")
        {
            var note = (request.Body ?? request.Note ?? "").Trim();
            if (note.Length == 0)
            {
                return Result<MeCard>.Fail("invalid_card");
            }

            person.DeviceStatus = "Screenshot";
            Mark(person, "equipment");
            evidenceId = await StoreAsync(db, "HR-01", "Device screenshot " + person.Name, "Employee portal", note, cancellationToken);
        }
        else
        {
            person.BackgroundStatus = "Clear";
            person.BackgroundOn = today;
            var note = string.IsNullOrWhiteSpace(request.Note) ? "Background check recorded." : request.Note.Trim();
            evidenceId = await StoreAsync(db, "HR-01", "Background check " + person.Name, "Employee portal", note, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "portal.completed", "ok", request.Kind, null, cancellationToken);
        var cards = await CardsAsync(cancellationToken);
        var card = cards.First(item => item.Kind == request.Kind);
        return Result<MeCard>.Ok(card with { EvidenceId = evidenceId ?? card.EvidenceId });
    }

    public async Task<IReadOnlyList<TrainingModuleItem>> ModulesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var modules = await db.TrainingModules.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var assigned = await db.People.CountAsync(item => item.State == PersonState.Active, cancellationToken);
        var counts = await db.TrainingCompletions.AsNoTracking().GroupBy(item => item.ModuleId).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        return modules.Select(item => new TrainingModuleItem(
            item.Id,
            item.Code,
            item.Title,
            item.ModuleType,
            item.AssignedGroup,
            item.DueOn,
            assigned,
            counts.FirstOrDefault(count => count.Key == item.Id)?.Count ?? 0,
            item.ExternalUrl)).ToArray();
    }

    public async Task<Result<TrainingModuleDetail>> ModuleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var module = await db.TrainingModules.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (module is null)
        {
            return Result<TrainingModuleDetail>.Fail("not_found");
        }

        var completion = await db.TrainingCompletions.AsNoTracking().FirstOrDefaultAsync(item => item.ModuleId == id && item.PersonEmail == tenant.Email, cancellationToken);
        var hash = await HashAsync(db, completion?.EvidenceId, cancellationToken);
        var slides = module.ModuleType == "BuiltIn"
            ? TrainingContent.Slides.Select(item => new TrainingSlideDto(item.Title, item.Body)).ToArray()
            : Array.Empty<TrainingSlideDto>();
        var questions = module.ModuleType == "BuiltIn"
            ? TrainingContent.Questions.Select(item => new TrainingQuestionDto(item.Prompt, item.Choices)).ToArray()
            : Array.Empty<TrainingQuestionDto>();
        return Result<TrainingModuleDetail>.Ok(new TrainingModuleDetail(
            module.Id,
            module.Code,
            module.Title,
            module.ModuleType,
            module.AssignedGroup,
            module.DueOn,
            module.ExternalUrl,
            slides,
            questions,
            completion is not null,
            completion?.Score,
            completion?.EvidenceId,
            hash));
    }

    public async Task<TrainingPersonPage> TrainingPeopleAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? 40 : pageSize, 1, 100);
        var index = Math.Max(page, 1);
        await using var db = Open();
        var module = await db.TrainingModules.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        var total = await db.People.CountAsync(cancellationToken);
        var people = await db.People.AsNoTracking().OrderBy(item => item.Name).Skip((index - 1) * size).Take(size).ToListAsync(cancellationToken);
        var emails = people.Select(item => item.Email).ToArray();
        var completions = module is null
            ? []
            : await db.TrainingCompletions.AsNoTracking().Where(item => item.ModuleId == id && emails.Contains(item.PersonEmail)).ToListAsync(cancellationToken);
        var items = people.Select(person =>
        {
            var completion = completions.FirstOrDefault(item => item.PersonEmail == person.Email);
            var status = completion is not null ? "Complete" : person.Training.ToString();
            return new TrainingPersonItem(person.Name, person.Email, person.Department, status, completion is null ? null : DateOnly.FromDateTime(completion.CompletedAt.UtcDateTime), person.TrainingDueOn);
        }).ToArray();
        return new TrainingPersonPage(items, total, index, size);
    }

    public async Task<Result<QuizResult>> SubmitQuizAsync(Guid id, QuizRequest request, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var module = await db.TrainingModules.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (module is null || module.ModuleType != "BuiltIn")
        {
            return Result<QuizResult>.Fail("not_found");
        }

        var existing = await db.TrainingCompletions.FirstOrDefaultAsync(item => item.ModuleId == id && item.PersonEmail == tenant.Email, cancellationToken);
        if (existing is not null)
        {
            return Result<QuizResult>.Ok(new QuizResult(true, existing.Score, TrainingContent.Questions.Count, existing.EvidenceId, await HashAsync(db, existing.EvidenceId, cancellationToken)));
        }

        var score = TrainingContent.Score(request.Answers ?? []);
        if (!TrainingContent.Passed(score))
        {
            return Result<QuizResult>.Ok(new QuizResult(false, score, TrainingContent.Questions.Count, null, null));
        }

        return Result<QuizResult>.Ok(await FinishModuleAsync(db, module, score, Certificate(module.Title, score), cancellationToken));
    }

    public async Task<Result<QuizResult>> UploadCertificateAsync(Guid id, CertificateRequest request, CancellationToken cancellationToken)
    {
        var body = (request.Body ?? "").Trim();
        if (body.Length == 0)
        {
            return Result<QuizResult>.Fail("invalid_certificate");
        }

        await using var db = Open();
        var module = await db.TrainingModules.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (module is null || module.ModuleType != "External")
        {
            return Result<QuizResult>.Fail("not_found");
        }

        var existing = await db.TrainingCompletions.FirstOrDefaultAsync(item => item.ModuleId == id && item.PersonEmail == tenant.Email, cancellationToken);
        if (existing is not null)
        {
            return Result<QuizResult>.Ok(new QuizResult(true, existing.Score, 1, existing.EvidenceId, await HashAsync(db, existing.EvidenceId, cancellationToken)));
        }

        return Result<QuizResult>.Ok(await FinishModuleAsync(db, module, 1, body, cancellationToken));
    }

    public async Task<AssetPage> AssetsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? 40 : pageSize, 1, 100);
        var index = Math.Max(page, 1);
        await using var db = Open();
        var query = db.Assets.AsNoTracking();
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Name, pattern)
                || EF.Functions.ILike(item.AssetType, pattern)
                || EF.Functions.ILike(item.Source, pattern)
                || EF.Functions.ILike(item.OwnerName, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(item => item.AssetType).ThenBy(item => item.Name).Skip((index - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new AssetPage(rows.Select(ToAsset).ToArray(), total, index, size);
    }

    public async Task<Result<AssetItem>> SaveAssetAsync(SaveAssetRequest request, CancellationToken cancellationToken)
    {
        if (!AssetTypes.Contains(request.Type) || !Classifications.Contains(request.Classification) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<AssetItem>.Fail("invalid_asset");
        }

        await using var db = Open();
        var row = request.Id is Guid id ? await db.Assets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken) : null;
        if (request.Id is not null && row is null)
        {
            return Result<AssetItem>.Fail("not_found");
        }

        row ??= new TrackedAsset { Id = Guid.NewGuid(), ExternalId = "manual:" + Guid.NewGuid().ToString("N"), LastSeen = time.GetUtcNow() };
        if (db.Entry(row).State == EntityState.Detached)
        {
            db.Assets.Add(row);
        }

        row.Name = request.Name.Trim();
        row.AssetType = request.Type;
        row.Source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();
        row.OwnerName = request.OwnerName.Trim();
        row.OwnerEmail = request.OwnerEmail.Trim();
        row.Environment = string.IsNullOrWhiteSpace(request.Environment) ? "production" : request.Environment.Trim();
        row.Classification = request.Classification;
        row.InScope = request.InScope;
        row.LastSeen = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "asset.saved", "ok", row.AssetType, null, cancellationToken);
        return Result<AssetItem>.Ok(ToAsset(row));
    }

    public async Task<Result<int>> ImportAssetsAsync(AssetImportRequest request, CancellationToken cancellationToken)
    {
        var lines = (request.Csv ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return Result<int>.Fail("invalid_asset");
        }

        await using var db = Open();
        var added = 0;
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            if (cells.Length < 6 || !AssetTypes.Contains(cells[1].Trim()))
            {
                continue;
            }

            var name = cells[0].Trim();
            var row = await db.Assets.FirstOrDefaultAsync(item => item.Name == name && item.Source == cells[2].Trim(), cancellationToken);
            row ??= new TrackedAsset { Id = Guid.NewGuid(), ExternalId = "import:" + Guid.NewGuid().ToString("N") };
            if (db.Entry(row).State == EntityState.Detached)
            {
                db.Assets.Add(row);
                added++;
            }

            row.Name = name;
            row.AssetType = cells[1].Trim();
            row.Source = cells[2].Trim();
            row.OwnerName = cells[3].Trim();
            row.OwnerEmail = cells.Length > 4 ? cells[4].Trim() : "";
            row.Environment = cells.Length > 5 ? cells[5].Trim() : "production";
            row.Classification = cells.Length > 6 && Classifications.Contains(cells[6].Trim()) ? cells[6].Trim() : "Internal";
            row.InScope = cells.Length <= 7 || !cells[7].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
            row.LastSeen = time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "asset.imported", "ok", added.ToString(CultureInfo.InvariantCulture), null, cancellationToken);
        return Result<int>.Ok(added);
    }

    public async Task<Result<FileExport>> ExportAssetsAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Assets.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("name,type,source,owner,ownerEmail,environment,classification,inScope,lastSeen");
        foreach (var row in rows)
        {
            builder.Append(Cell(row.Name)).Append(',')
                .Append(Cell(row.AssetType)).Append(',')
                .Append(Cell(row.Source)).Append(',')
                .Append(Cell(row.OwnerName)).Append(',')
                .Append(Cell(row.OwnerEmail)).Append(',')
                .Append(Cell(row.Environment)).Append(',')
                .Append(Cell(row.Classification)).Append(',')
                .Append(row.InScope ? "true" : "false").Append(',')
                .Append(row.LastSeen.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "asset.exported", "ok", "csv", null, cancellationToken);
        return Result<FileExport>.Ok(new FileExport("assets.csv", "text/csv", Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public async Task<IReadOnlyList<WorkTaskItem>> TasksAsync(bool mine, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.WorkTasks.AsNoTracking().ToListAsync(cancellationToken);
        if (mine)
        {
            rows = rows.Where(item => item.OwnerEmail.Equals(tenant.Email, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rows.OrderBy(item => item.Status == "Done").ThenBy(item => item.DueOn).Select(item => ToTask(item, Today())).ToArray();
    }

    public async Task<Result<WorkTaskItem>> SaveTaskAsync(SaveTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || !TaskKinds.Contains(request.LinkedKind) || !TaskStatuses.Contains(request.Status) || !Recurrences.Contains(request.Recurrence))
        {
            return Result<WorkTaskItem>.Fail("invalid_task");
        }

        await using var db = Open();
        var row = request.Id is Guid id ? await db.WorkTasks.FirstOrDefaultAsync(item => item.Id == id, cancellationToken) : null;
        if (request.Id is not null && row is null)
        {
            return Result<WorkTaskItem>.Fail("not_found");
        }

        row ??= new WorkTask { Id = Guid.NewGuid() };
        if (db.Entry(row).State == EntityState.Detached)
        {
            db.WorkTasks.Add(row);
        }

        row.Title = request.Title.Trim();
        row.LinkedKind = request.LinkedKind;
        row.LinkedCode = (request.LinkedCode ?? "").Trim();
        row.OwnerName = request.OwnerName.Trim();
        row.OwnerEmail = request.OwnerEmail.Trim();
        row.DueOn = request.DueOn;
        row.Status = request.Status;
        row.Recurrence = request.Recurrence;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "task.saved", "ok", row.LinkedKind, null, cancellationToken);
        return Result<WorkTaskItem>.Ok(ToTask(row, Today()));
    }

    public async Task<Result<WorkTaskItem>> FinishTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.WorkTasks.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<WorkTaskItem>.Fail("not_found");
        }

        if (!tenant.CanWrite && !row.OwnerEmail.Equals(tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<WorkTaskItem>.Fail("forbidden");
        }

        row.Status = "Done";
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "task.done", "ok", row.LinkedKind, null, cancellationToken);
        return Result<WorkTaskItem>.Ok(ToTask(row, Today()));
    }

    public async Task<IReadOnlyList<MemberItem>> MembersAsync(CancellationToken cancellationToken)
    {
        var rows = await control.Memberships.AsNoTracking().Include(item => item.User).Where(item => item.TenantId == tenant.TenantId).ToListAsync(cancellationToken);
        return rows.OrderBy(item => item.User?.DisplayName, StringComparer.Ordinal).Select(ToMember).ToArray();
    }

    public async Task<Result<MemberItem>> InviteAsync(InviteRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<MemberItem>.Fail("forbidden");
        }

        var email = (request.Email ?? "").Trim();
        var name = (request.Name ?? "").Trim();
        if (!Enum.TryParse<TenantRole>(request.Role, out var role) || name.Length == 0 || email.Length is < 3 or > 320 || !email.Contains('@'))
        {
            return Result<MemberItem>.Fail("invalid_member");
        }

        var user = await control.Users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        if (user is null)
        {
            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = name,
                Subject = "local:" + Guid.NewGuid().ToString("N"),
                CreatedAt = time.GetUtcNow(),
            };
            user.PasswordHash = passwords.HashPassword(user, Guid.NewGuid().ToString("N"));
            control.Users.Add(user);
        }

        var membership = await control.Memberships.FirstOrDefaultAsync(item => item.UserId == user.Id && item.TenantId == tenant.TenantId, cancellationToken);
        if (membership is null)
        {
            membership = new Membership { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenant.TenantId };
            control.Memberships.Add(membership);
        }

        membership.Role = role;
        membership.Active = true;
        membership.User = user;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "member.invited", "ok", role.ToString(), null, cancellationToken);
        return Result<MemberItem>.Ok(ToMember(membership));
    }

    public async Task<Result<MemberItem>> SetRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin || !Enum.TryParse<TenantRole>(request.Role, out var role))
        {
            return Result<MemberItem>.Fail(tenant.IsAdmin ? "invalid_member" : "forbidden");
        }

        var membership = await control.Memberships.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenant.TenantId, cancellationToken);
        if (membership is null)
        {
            return Result<MemberItem>.Fail("not_found");
        }

        membership.Role = role;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "member.role", "ok", role.ToString(), null, cancellationToken);
        return Result<MemberItem>.Ok(ToMember(membership));
    }

    public async Task<Result<MemberItem>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<MemberItem>.Fail("forbidden");
        }

        var membership = await control.Memberships.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenant.TenantId, cancellationToken);
        if (membership is null)
        {
            return Result<MemberItem>.Fail("not_found");
        }

        if (membership.UserId == tenant.UserId)
        {
            return Result<MemberItem>.Fail("invalid_member");
        }

        membership.Active = false;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "member.deactivated", "ok", membership.Role.ToString(), null, cancellationToken);
        return Result<MemberItem>.Ok(ToMember(membership));
    }

    public async Task<IReadOnlyList<SsoStatus>> SsoAsync(CancellationToken cancellationToken)
    {
        var domains = await control.TenantDomains.AsNoTracking().Where(item => item.TenantId == tenant.TenantId).ToListAsync(cancellationToken);
        if (domains.Count == 0)
        {
            var email = tenant.Email;
            var at = email.IndexOf('@');
            var domain = at > 0 ? email[(at + 1)..] : "";
            return [new SsoStatus(domain, false, null)];
        }

        return domains.Select(item => new SsoStatus(item.Domain, !string.IsNullOrWhiteSpace(item.WorkOsOrganizationId), item.WorkOsOrganizationId)).ToArray();
    }

    public async Task<IReadOnlyList<GrantItem>> GrantsAsync(CancellationToken cancellationToken)
    {
        var grants = await control.AuditorGrants.AsNoTracking().Include(item => item.Frameworks).Where(item => item.TenantId == tenant.TenantId).ToListAsync(cancellationToken);
        var users = await control.Users.AsNoTracking().Where(item => grants.Select(grant => grant.UserId).Contains(item.Id)).ToListAsync(cancellationToken);
        return grants.Select(grant =>
        {
            var user = users.FirstOrDefault(item => item.Id == grant.UserId);
            return new GrantItem(grant.Id, user?.Email ?? "", user?.DisplayName ?? "", grant.AuditName, grant.FirmName, grant.PeriodStart, grant.PeriodEnd, grant.AccessExpiresOn, grant.Frameworks.Select(item => item.FrameworkKey).Order(StringComparer.Ordinal).ToArray());
        }).ToArray();
    }

    public async Task<Result<GrantItem>> SaveGrantAsync(SaveGrantRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<GrantItem>.Fail("forbidden");
        }

        var email = (request.Email ?? "").Trim();
        var user = await control.Users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        var member = user is null ? null : await control.Memberships.FirstOrDefaultAsync(item => item.UserId == user.Id && item.TenantId == tenant.TenantId, cancellationToken);
        if (user is null || member is null || request.Frameworks is null || request.Frameworks.Count == 0)
        {
            return Result<GrantItem>.Fail("invalid_grant");
        }

        var grant = await control.AuditorGrants.Include(item => item.Frameworks).FirstOrDefaultAsync(item => item.UserId == user.Id && item.TenantId == tenant.TenantId && item.AuditName == request.AuditName, cancellationToken);
        if (grant is null)
        {
            grant = new AuditorGrant { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenant.TenantId };
            control.AuditorGrants.Add(grant);
        }

        grant.AuditName = request.AuditName.Trim();
        grant.FirmName = request.FirmName.Trim();
        grant.PeriodStart = request.PeriodStart;
        grant.PeriodEnd = request.PeriodEnd;
        grant.AccessExpiresOn = request.AccessExpiresOn;
        control.AuditorGrantFrameworks.RemoveRange(grant.Frameworks);
        grant.Frameworks.Clear();
        foreach (var key in request.Frameworks.Distinct(StringComparer.Ordinal))
        {
            var link = new AuditorGrantFramework { GrantId = grant.Id, FrameworkKey = key };
            grant.Frameworks.Add(link);
            control.AuditorGrantFrameworks.Add(link);
        }

        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "grant.saved", "ok", grant.AuditName, null, cancellationToken);
        return Result<GrantItem>.Ok(new GrantItem(grant.Id, user.Email, user.DisplayName, grant.AuditName, grant.FirmName, grant.PeriodStart, grant.PeriodEnd, grant.AccessExpiresOn, grant.Frameworks.Select(item => item.FrameworkKey).ToArray()));
    }

    private async Task<QuizResult> FinishModuleAsync(TenantDbContext db, TrainingModule module, int score, string body, CancellationToken cancellationToken)
    {
        var person = await FindPersonAsync(db, tenant.Email, cancellationToken);
        var evidenceId = await StoreAsync(db, "HR-06", module.Title + " " + (person?.Name ?? "employee"), "Security training", body, cancellationToken);
        var completion = new TrainingCompletion
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            PersonEmail = tenant.Email,
            CompletedAt = time.GetUtcNow(),
            Score = score,
            EvidenceId = evidenceId == Guid.Empty ? null : evidenceId,
        };
        db.TrainingCompletions.Add(completion);
        if (person is not null && module.Code == TrainingContent.AnnualCode)
        {
            person.Training = TrainingState.Complete;
            person.TrainingCompletedOn = Today();
            Mark(person, "training");
        }

        await RefreshTrainingTestAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "training.completed", "ok", module.Code, null, cancellationToken);
        return new QuizResult(true, score, module.ModuleType == "BuiltIn" ? TrainingContent.Questions.Count : 1, completion.EvidenceId, await HashAsync(db, completion.EvidenceId, cancellationToken));
    }

    private string Certificate(string title, int score)
    {
        return title + " certificate. " + tenant.DisplayName + " completed the module on " + Today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ". Score " + score.ToString(CultureInfo.InvariantCulture) + " of " + TrainingContent.Questions.Count.ToString(CultureInfo.InvariantCulture) + ".";
    }

    private async Task RefreshTrainingTestAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var people = await db.People.AsNoTracking().Select(item => new { item.State, item.TrainingCompletedOn }).ToListAsync(cancellationToken);
        var current = TrainingRules.OrganizationCurrent(people.Select(item => (item.State.ToString(), item.TrainingCompletedOn)), Today());
        var controlRow = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "HR-06", cancellationToken);
        if (controlRow is null)
        {
            return;
        }

        controlRow.Status = current ? ControlStatus.Passing : ControlStatus.Failing;
        var test = controlRow.Tests.FirstOrDefault(item => item.Name == "Personnel completed training within 12 months");
        if (test is null)
        {
            return;
        }

        test.Outcome = current ? TestOutcome.Pass : TestOutcome.Fail;
        test.Detail = current ? "Personnel completed training within 12 months." : "Some personnel are outside the 12-month training window.";
    }

    private static async Task RefreshMfaTestAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var people = await db.People.AsNoTracking().Where(item => item.State == PersonState.Active).Select(item => item.MfaEnrolled).ToListAsync(cancellationToken);
        if (people.Count == 0 || people.Any(item => !item))
        {
            return;
        }

        var test = await db.ControlTests.FirstOrDefaultAsync(item => item.Name.Contains("MFA"), cancellationToken);
        if (test is null)
        {
            return;
        }

        test.Outcome = TestOutcome.Pass;
        test.Detail = "Active personnel have a second factor enrolled.";
    }

    private async Task<Guid> StoreAsync(TenantDbContext db, string controlCode, string title, string source, string body, CancellationToken cancellationToken)
    {
        var controlId = await db.Controls.Where(item => item.Code == controlCode).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await db.Controls.OrderBy(item => item.Code).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        if (controlId is null)
        {
            return Guid.Empty;
        }

        var years = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstAsync(cancellationToken);
        var evidenceId = Guid.NewGuid();
        var stored = new EvidenceItem
        {
            Id = evidenceId,
            ControlId = controlId.Value,
            Title = title,
            Source = source,
            ObjectKey = tenant.TenantId.ToString("N") + "/" + evidenceId.ToString("N") + ".txt",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(stored);
        await evidence.AppendAsync(db, tenant.TenantId, stored, Encoding.UTF8.GetBytes(body), "text/plain", time.GetUtcNow().AddYears(years), cancellationToken);
        return evidenceId;
    }

    private static void Mark(DirectoryPerson person, string code)
    {
        var task = person.Tasks.FirstOrDefault(item => item.TaskCode == code);
        if (task is not null)
        {
            task.Done = true;
        }
    }

    private static async Task<DirectoryPerson?> FindPersonAsync(TenantDbContext db, string email, CancellationToken cancellationToken) =>
        await db.People.Include(item => item.Tasks).FirstOrDefaultAsync(item => item.Email == email, cancellationToken);

    private static async Task<List<PolicyAcknowledgement>> AcknowledgementsAsync(TenantDbContext db, string email, CancellationToken cancellationToken) =>
        await db.Acknowledgements.AsNoTracking().Where(item => item.PersonEmail == email).ToListAsync(cancellationToken);

    private static async Task<(Guid? Evidence, bool Done)> CompletionAsync(TenantDbContext db, string code, string email, CancellationToken cancellationToken)
    {
        var module = await db.TrainingModules.AsNoTracking().FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (module is null)
        {
            return (null, false);
        }

        var completion = await db.TrainingCompletions.AsNoTracking().FirstOrDefaultAsync(item => item.ModuleId == module.Id && item.PersonEmail == email, cancellationToken);
        return (completion?.EvidenceId, completion is not null);
    }

    private async Task<IReadOnlyList<MeCard>> WithHashesAsync(TenantDbContext db, List<MeCard> cards, CancellationToken cancellationToken)
    {
        var result = new List<MeCard>();
        foreach (var card in cards)
        {
            result.Add(card with { EvidenceSha256 = await HashAsync(db, card.EvidenceId, cancellationToken) });
        }

        return result;
    }

    private static async Task<string?> HashAsync(TenantDbContext db, Guid? evidenceId, CancellationToken cancellationToken)
    {
        if (evidenceId is null)
        {
            return null;
        }

        return await db.Evidence.AsNoTracking().Where(item => item.Id == evidenceId).Select(item => item.Sha256).FirstOrDefaultAsync(cancellationToken);
    }

    private static MeCard Card(string kind, string status, DateOnly? due, string action, Guid? evidenceId) =>
        new(kind, CardTitle(kind), status, due, action, evidenceId, null);

    private static string CardTitle(string kind) => kind switch
    {
        "Policy" => "Policies to acknowledge",
        "Training" => "Security training",
        "Mfa" => "Multi-factor authentication",
        "Device" => "Device",
        "BackgroundCheck" => "Background check",
        _ => kind,
    };

    private static string AckStatus(IReadOnlyList<PolicyAcknowledgement> open, DateOnly today)
    {
        if (open.Count == 0)
        {
            return "Complete";
        }

        return open.Any(item => item.DueOn < today) ? "Overdue" : "Due";
    }

    private static string BackgroundLabel(string status)
    {
        if (status == "Clear")
        {
            return "Complete";
        }

        return status == "Pending" ? "Due" : "NotStarted";
    }

    private static string TrainingStatus(DirectoryPerson person)
    {
        if (person.Training == TrainingState.Complete)
        {
            return "Complete";
        }

        if (person.Training == TrainingState.Overdue)
        {
            return "Overdue";
        }

        return person.Training == TrainingState.Assigned ? "Due" : "NotStarted";
    }

    private static AssetItem ToAsset(TrackedAsset row) => new(row.Id, row.Name, row.AssetType, row.Source, row.OwnerName, row.OwnerEmail, row.Environment, row.Classification, row.InScope, row.LastSeen);

    private static WorkTaskItem ToTask(WorkTask row, DateOnly today) =>
        new(row.Id, row.Title, row.LinkedKind, row.LinkedCode, row.OwnerName, row.OwnerEmail, row.DueOn, row.Status, row.Recurrence, row.Status != "Done" && row.DueOn < today);

    private static MemberItem ToMember(Membership row) => new(row.Id, row.User?.DisplayName ?? "", row.User?.Email ?? "", row.Role.ToString(), row.Active);

    private static string Cell(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);
}
