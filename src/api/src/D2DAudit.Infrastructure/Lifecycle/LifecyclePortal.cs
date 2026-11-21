using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.Lifecycle;

public sealed class LifecyclePortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    IAutomationDispatcher automation,
    IAccessRevoker revoker,
    IConfiguration configuration,
    TimeProvider time) : ILifecycle
{
    private const int HrisLookbackDays = 7;
    private const int TestWindowDays = 180;
    private const string Open = "Open";
    private const string Complete = "Complete";

    public async Task<LifecycleOverviewDto> OverviewAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var settings = await db.LifecycleSettings.AsNoTracking().FirstAsync(cancellationToken);
        var groups = await db.EmployeeGroups.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking()
            .Where(item => item.State != PersonState.Offboarded)
            .Select(item => new { item.Department, item.Role })
            .ToListAsync(cancellationToken);
        var templates = await db.LifecycleTemplateSteps.AsNoTracking().OrderBy(item => item.Kind).ThenBy(item => item.Order).ToListAsync(cancellationToken);
        var horizon = time.GetUtcNow().AddDays(-TestWindowDays);
        var checklists = await db.LifecycleChecklists.AsNoTracking()
            .Include(item => item.Steps)
            .Where(item => item.State == Open || item.TriggeredAt >= horizon)
            .OrderByDescending(item => item.TriggeredAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var lookup = await DepartmentsAsync(db, checklists.Select(item => item.PersonId), cancellationToken);
        return new LifecycleOverviewDto(
            ToSettings(settings),
            groups.Select(group => ToGroup(group, people.Count(person => LifecycleRules.InGroup(group.Departments, group.RoleKeywords, person.Department, person.Role)))).ToArray(),
            templates.Select(ToTemplate).ToArray(),
            checklists.Select(item => ToChecklist(item, GroupsOf(groups, lookup, item.PersonId))).ToArray(),
            await TestsAsync(db, cancellationToken),
            ConfigurableAccessRevoker.OktaConfigured(configuration) ? "okta-live" : "mock");
    }

    public async Task<Result<LifecycleSettingsDto>> SaveSettingsAsync(LifecycleSettingsDto request, CancellationToken cancellationToken)
    {
        if (request.OffboardingAccessSlaHours is < 1 or > 720
            || request.OffboardingCompleteSlaHours is < 1 or > 1440
            || request.OffboardingCompleteSlaHours < request.OffboardingAccessSlaHours
            || request.OnboardingDays is < 1 or > 120)
        {
            return Result<LifecycleSettingsDto>.Fail("invalid_settings");
        }

        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var settings = await db.LifecycleSettings.FirstAsync(cancellationToken);
        settings.OffboardingAccessSlaHours = request.OffboardingAccessSlaHours;
        settings.OffboardingCompleteSlaHours = request.OffboardingCompleteSlaHours;
        settings.OnboardingDays = request.OnboardingDays;
        settings.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.settings", $"access {settings.OffboardingAccessSlaHours}h, offboarding {settings.OffboardingCompleteSlaHours}h, onboarding {settings.OnboardingDays}d", cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<LifecycleSettingsDto>.Ok(ToSettings(settings));
    }

    public async Task<Result<EmployeeGroupDto>> SaveGroupAsync(SaveEmployeeGroupRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? "").Trim().ToLowerInvariant();
        var name = (request.Name ?? "").Trim();
        if (code.Length is 0 or > 64 || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') || name.Length is 0 or > 120 || (request.Description ?? "").Length > 500)
        {
            return Result<EmployeeGroupDto>.Fail("invalid_group");
        }

        var departments = Csv(request.Departments);
        var roles = Csv(request.RoleKeywords);
        var policies = Csv(request.PolicyCodes);
        var training = Csv(request.TrainingCodes);
        if (new[] { departments, roles, policies, training }.Any(item => item is null))
        {
            return Result<EmployeeGroupDto>.Fail("invalid_group");
        }

        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var group = await db.EmployeeGroups.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (group is null)
        {
            group = new EmployeeGroup { Id = Guid.NewGuid(), Code = code };
            db.EmployeeGroups.Add(group);
        }

        group.Name = name;
        group.Description = (request.Description ?? "").Trim();
        group.Departments = departments!;
        group.RoleKeywords = roles!;
        group.PolicyCodes = policies!;
        group.TrainingCodes = training!;
        group.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.group", code, cancellationToken);
        var people = await db.People.AsNoTracking().Where(item => item.State != PersonState.Offboarded).Select(item => new { item.Department, item.Role }).ToListAsync(cancellationToken);
        return Result<EmployeeGroupDto>.Ok(ToGroup(group, people.Count(person => LifecycleRules.InGroup(group.Departments, group.RoleKeywords, person.Department, person.Role))));
    }

    public async Task<Result<LifecycleTemplateStepDto>> SaveTemplateStepAsync(SaveLifecycleTemplateStepRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? "").Trim().ToLowerInvariant();
        var title = (request.Title ?? "").Trim();
        var category = (request.Category ?? "").Trim();
        var groupCode = (request.GroupCode ?? "").Trim().ToLowerInvariant();
        if (!Enum.TryParse<LifecycleKind>(request.Kind, out var kind)
            || code.Length is 0 or > 64
            || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')
            || title.Length is 0 or > 200
            || category.Length is 0 or > 64
            || groupCode.Length > 64
            || request.SlaHours is < 0 or > 24 * 120
            || (kind == LifecycleKind.Onboarding && request.RevokesAccess))
        {
            return Result<LifecycleTemplateStepDto>.Fail("invalid_template");
        }

        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        if (groupCode.Length > 0 && !await db.EmployeeGroups.AnyAsync(item => item.Code == groupCode, cancellationToken))
        {
            return Result<LifecycleTemplateStepDto>.Fail("invalid_template");
        }

        LifecycleTemplateStep? step;
        if (request.Id is Guid id)
        {
            step = await db.LifecycleTemplateSteps.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (step is null)
            {
                return Result<LifecycleTemplateStepDto>.Fail("not_found");
            }
        }
        else
        {
            var order = await db.LifecycleTemplateSteps.Where(item => item.Kind == kind).Select(item => (int?)item.Order).MaxAsync(cancellationToken) ?? -1;
            step = new LifecycleTemplateStep { Id = Guid.NewGuid(), Order = order + 1 };
            db.LifecycleTemplateSteps.Add(step);
        }

        if (await db.LifecycleTemplateSteps.AnyAsync(item => item.Id != step.Id && item.Kind == kind && item.Code == code && item.GroupCode == groupCode, cancellationToken))
        {
            return Result<LifecycleTemplateStepDto>.Fail("duplicate_template");
        }

        step.Kind = kind;
        step.Code = code;
        step.Title = title;
        step.Category = category;
        step.SlaHours = request.SlaHours;
        step.GroupCode = groupCode;
        step.RevokesAccess = request.RevokesAccess;
        step.PerAccount = request.PerAccount;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.template", $"{kind} {code}", cancellationToken);
        return Result<LifecycleTemplateStepDto>.Ok(ToTemplate(step));
    }

    public async Task<Result<bool>> DeleteTemplateStepAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var step = await db.LifecycleTemplateSteps.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (step is null)
        {
            return Result<bool>.Fail("not_found");
        }

        db.LifecycleTemplateSteps.Remove(step);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.template.delete", $"{step.Kind} {step.Code}", cancellationToken);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<LifecycleSyncResult>> SyncHrisAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var settings = await db.LifecycleSettings.AsNoTracking().FirstAsync(cancellationToken);
        var today = Today();
        var leaverFrom = today.AddDays(-HrisLookbackDays);
        var hireFrom = today.AddDays(-settings.OnboardingDays);
        var horizon = today.AddDays(30);
        var started = await db.LifecycleChecklists.AsNoTracking().Select(item => new { item.PersonId, item.Kind }).ToListAsync(cancellationToken);
        var leavers = await db.People.Include(item => item.Accounts)
            .Where(item => item.LeftOn != null && item.LeftOn >= leaverFrom && item.LeftOn <= horizon && item.State != PersonState.Offboarded)
            .ToListAsync(cancellationToken);
        var hires = await db.People.Include(item => item.Accounts)
            .Where(item => item.HireOn != null && item.HireOn >= hireFrom && item.HireOn <= horizon && item.LeftOn == null && item.State == PersonState.Active)
            .ToListAsync(cancellationToken);
        var context = await GenerationContextAsync(db, cancellationToken);
        var offboarding = 0;
        foreach (var person in leavers.Where(item => !started.Any(row => row.PersonId == item.Id && row.Kind == LifecycleKind.Offboarding)))
        {
            Start(db, context, person, LifecycleKind.Offboarding, "Hris", "Termination date from the HR system", AtStartOfDay(person.LeftOn!.Value), person.LeftOn.Value);
            person.State = PersonState.Offboarding;
            offboarding++;
        }

        var onboarding = 0;
        foreach (var person in hires.Where(item => !started.Any(row => row.PersonId == item.Id && row.Kind == LifecycleKind.Onboarding)))
        {
            Start(db, context, person, LifecycleKind.Onboarding, "Hris", "Start date from the HR system", AtStartOfDay(person.HireOn!.Value), person.HireOn.Value);
            onboarding++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.sync", $"{onboarding} onboarding, {offboarding} offboarding", cancellationToken);
        var tests = await RefreshAsync(db, cancellationToken);
        return Result<LifecycleSyncResult>.Ok(new LifecycleSyncResult(onboarding, offboarding, tests));
    }

    public async Task<Result<PersonLifecycleDto>> PersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(item => item.Id == personId, cancellationToken);
        if (person is null)
        {
            return Result<PersonLifecycleDto>.Fail("not_found");
        }

        return Result<PersonLifecycleDto>.Ok(await ToPersonAsync(db, person, cancellationToken));
    }

    public async Task<Result<LifecycleChecklistDto>> StartOffboardingAsync(Guid personId, StartOffboardingRequest request, CancellationToken cancellationToken)
    {
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length > 500)
        {
            return Result<LifecycleChecklistDto>.Fail("invalid_offboarding");
        }

        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var person = await db.People.Include(item => item.Accounts).FirstOrDefaultAsync(item => item.Id == personId, cancellationToken);
        if (person is null)
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        var existing = await db.LifecycleChecklists.Include(item => item.Steps)
            .FirstOrDefaultAsync(item => item.PersonId == personId && item.Kind == LifecycleKind.Offboarding && item.State == Open, cancellationToken);
        if (existing is not null)
        {
            return Result<LifecycleChecklistDto>.Fail("already_offboarding");
        }

        var termination = request.TerminationOn ?? Today();
        var context = await GenerationContextAsync(db, cancellationToken);
        var checklist = Start(db, context, person, LifecycleKind.Offboarding, "Manual", reason.Length > 0 ? reason : "Offboarding started manually", time.GetUtcNow(), termination);
        person.LeftOn = termination;
        person.State = PersonState.Offboarding;
        person.StatusReason = checklist.Reason;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.offboard", person.Email, cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<LifecycleChecklistDto>.Ok(ToChecklist(checklist, GroupsOf(context.Groups, person)));
    }

    public async Task<Result<LifecycleChecklistDto>> StartOnboardingAsync(Guid personId, StartOnboardingRequest request, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var person = await db.People.Include(item => item.Accounts).FirstOrDefaultAsync(item => item.Id == personId, cancellationToken);
        if (person is null)
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        if (person.State != PersonState.Active)
        {
            return Result<LifecycleChecklistDto>.Fail("invalid_onboarding");
        }

        if (await db.LifecycleChecklists.AnyAsync(item => item.PersonId == personId && item.Kind == LifecycleKind.Onboarding && item.State == Open, cancellationToken))
        {
            return Result<LifecycleChecklistDto>.Fail("already_onboarding");
        }

        var start = request.StartOn ?? person.HireOn ?? Today();
        var triggered = start > Today() ? AtStartOfDay(start) : time.GetUtcNow();
        var context = await GenerationContextAsync(db, cancellationToken);
        var checklist = Start(db, context, person, LifecycleKind.Onboarding, "Manual", "Onboarding started manually", triggered, start);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.onboard", person.Email, cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<LifecycleChecklistDto>.Ok(ToChecklist(checklist, GroupsOf(context.Groups, person)));
    }

    public async Task<Result<LifecycleChecklistDto>> CompleteStepAsync(Guid stepId, CompleteLifecycleStepRequest request, CancellationToken cancellationToken)
    {
        var note = (request.Note ?? "").Trim();
        if (note.Length > 500)
        {
            return Result<LifecycleChecklistDto>.Fail("invalid_step");
        }

        await using var db = OpenDb();
        var step = await db.LifecycleSteps.FirstOrDefaultAsync(item => item.Id == stepId, cancellationToken);
        if (step is null)
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        var checklist = await db.LifecycleChecklists.Include(item => item.Steps).FirstAsync(item => item.Id == step.ChecklistId, cancellationToken);
        var result = note.Length > 0 ? note : "Marked done";
        Mark(step, request.Done, request.Done ? result : "");
        await SettleAsync(db, checklist, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.step", $"{checklist.PersonEmail} {step.Code} {(request.Done ? "done" : "reopened")}", cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<LifecycleChecklistDto>.Ok(await ChecklistDtoAsync(db, checklist, cancellationToken));
    }

    public async Task<Result<LifecycleChecklistDto>> RevokeStepAsync(Guid stepId, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var step = await db.LifecycleSteps.FirstOrDefaultAsync(item => item.Id == stepId, cancellationToken);
        if (step is null)
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        if (!step.RevokesAccess)
        {
            return Result<LifecycleChecklistDto>.Fail("not_revocable");
        }

        var checklist = await db.LifecycleChecklists.Include(item => item.Steps).FirstAsync(item => item.Id == step.ChecklistId, cancellationToken);
        if (!step.Done)
        {
            await RevokeAsync(db, checklist, step, cancellationToken);
            await SettleAsync(db, checklist, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await RefreshAsync(db, cancellationToken);
        }

        return Result<LifecycleChecklistDto>.Ok(await ChecklistDtoAsync(db, checklist, cancellationToken));
    }

    public async Task<Result<RevokeAllResult>> RevokeAllAsync(Guid checklistId, CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        var checklist = await db.LifecycleChecklists.Include(item => item.Steps).FirstOrDefaultAsync(item => item.Id == checklistId, cancellationToken);
        if (checklist is null)
        {
            return Result<RevokeAllResult>.Fail("not_found");
        }

        if (checklist.Kind != LifecycleKind.Offboarding)
        {
            return Result<RevokeAllResult>.Fail("not_revocable");
        }

        var revoked = 0;
        var failed = 0;
        foreach (var step in checklist.Steps.Where(item => item.RevokesAccess && !item.Done).OrderBy(item => item.Order).ToArray())
        {
            if (await RevokeAsync(db, checklist, step, cancellationToken))
            {
                revoked++;
            }
            else
            {
                failed++;
            }
        }

        await SettleAsync(db, checklist, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<RevokeAllResult>.Ok(new RevokeAllResult(revoked, failed, await ChecklistDtoAsync(db, checklist, cancellationToken)));
    }

    public async Task<Result<IReadOnlyList<LifecycleTestDto>>> RefreshTestsAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        return Result<IReadOnlyList<LifecycleTestDto>>.Ok(await RefreshAsync(db, cancellationToken));
    }

    public async Task<PersonLifecycleDto?> MineAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor || string.IsNullOrWhiteSpace(tenant.Email))
        {
            return null;
        }

        await using var db = OpenDb();
        await EnsureSeededAsync(db, cancellationToken);
        var email = tenant.Email.Trim().ToLowerInvariant();
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
        return person is null ? null : await ToPersonAsync(db, person, cancellationToken);
    }

    public async Task<Result<LifecycleChecklistDto>> CompleteMyStepAsync(Guid stepId, CompleteLifecycleStepRequest request, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<LifecycleChecklistDto>.Fail("forbidden");
        }

        await using var db = OpenDb();
        var step = await db.LifecycleSteps.FirstOrDefaultAsync(item => item.Id == stepId, cancellationToken);
        if (step is null)
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        var checklist = await db.LifecycleChecklists.Include(item => item.Steps).FirstAsync(item => item.Id == step.ChecklistId, cancellationToken);
        if (!string.Equals(checklist.PersonEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result<LifecycleChecklistDto>.Fail("not_found");
        }

        if (checklist.Kind != LifecycleKind.Onboarding || !LifecycleSelfService.CanComplete(step.Category))
        {
            return Result<LifecycleChecklistDto>.Fail("forbidden");
        }

        Mark(step, request.Done, request.Done ? "Confirmed by the employee" : "");
        await SettleAsync(db, checklist, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("lifecycle.self", $"{step.Code} {(request.Done ? "done" : "reopened")}", cancellationToken);
        await RefreshAsync(db, cancellationToken);
        return Result<LifecycleChecklistDto>.Ok(await ChecklistDtoAsync(db, checklist, cancellationToken));
    }

    private sealed record GenerationContext(LifecycleSettings Settings, IReadOnlyList<EmployeeGroup> Groups, IReadOnlyList<LifecycleTemplateSeed> Templates);

    private static async Task<GenerationContext> GenerationContextAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.LifecycleSettings.AsNoTracking().FirstAsync(cancellationToken);
        var groups = await db.EmployeeGroups.AsNoTracking().ToListAsync(cancellationToken);
        var templates = await db.LifecycleTemplateSteps.AsNoTracking().OrderBy(item => item.Order).ToListAsync(cancellationToken);
        return new GenerationContext(
            settings,
            groups,
            templates.Select(item => new LifecycleTemplateSeed(item.Kind, item.Code, item.Title, item.Category, item.SlaHours, item.RevokesAccess, item.PerAccount, item.GroupCode)).ToArray());
    }

    private LifecycleChecklist Start(
        TenantDbContext db,
        GenerationContext context,
        DirectoryPerson person,
        LifecycleKind kind,
        string trigger,
        string reason,
        DateTimeOffset triggeredAt,
        DateOnly effectiveOn)
    {
        var groups = GroupsOf(context.Groups, person);
        var accounts = person.Accounts.Select(item => new LifecycleAccount(item.Provider, item.AccountName, item.Active)).ToArray();
        var generated = LifecycleRules.Generate(
            kind,
            context.Templates,
            groups,
            accounts,
            triggeredAt,
            context.Settings.OffboardingAccessSlaHours,
            context.Settings.OffboardingCompleteSlaHours,
            context.Settings.OnboardingDays);
        var checklist = new LifecycleChecklist
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            PersonName = person.Name,
            PersonEmail = person.Email,
            Kind = kind,
            Trigger = trigger,
            Reason = reason,
            EffectiveOn = effectiveOn,
            TriggeredAt = triggeredAt,
            DueAt = LifecycleRules.CompleteDue(kind, triggeredAt, context.Settings.OffboardingCompleteSlaHours, context.Settings.OnboardingDays),
            AccessDueAt = kind == LifecycleKind.Offboarding ? LifecycleRules.AccessDue(triggeredAt, context.Settings.OffboardingAccessSlaHours) : null,
            State = Open,
            StartedBy = trigger == "Hris" ? "HR system" : Actor(),
        };
        foreach (var item in generated)
        {
            checklist.Steps.Add(new LifecycleStep
            {
                Id = Guid.NewGuid(),
                ChecklistId = checklist.Id,
                Code = item.Code,
                Title = item.Title,
                Category = item.Category,
                Provider = item.Provider,
                AccountName = item.AccountName,
                RevokesAccess = item.RevokesAccess,
                Order = item.Order,
                DueAt = item.DueAt,
            });
        }

        db.LifecycleChecklists.Add(checklist);
        return checklist;
    }

    private async Task<bool> RevokeAsync(TenantDbContext db, LifecycleChecklist checklist, LifecycleStep step, CancellationToken cancellationToken)
    {
        var provider = step.Provider.Length > 0 ? step.Provider : ProviderFor(step.Code);
        var result = await revoker.RevokeAsync(new AccessRevocationRequest(checklist.PersonEmail, checklist.PersonName, provider, step.AccountName, step.Code), cancellationToken);
        step.Result = Trim($"{result.Receipt} {result.Detail}".Trim(), 1000);
        if (!result.Succeeded)
        {
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "lifecycle.revoke", "failed", $"{checklist.PersonEmail} {provider} {step.AccountName}".Trim(), null, cancellationToken);
            return false;
        }

        var now = time.GetUtcNow();
        step.Done = true;
        step.CompletedAt = now;
        step.CompletedBy = Actor();
        var accounts = await db.PersonAccounts
            .Where(item => item.PersonId == checklist.PersonId && item.Active)
            .ToListAsync(cancellationToken);
        foreach (var account in accounts.Where(item => Matches(item, step)))
        {
            account.Active = false;
        }

        step.EvidenceId = await WriteEvidenceAsync(db, checklist, step, provider, result, now, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "lifecycle.revoke", "ok", $"{checklist.PersonEmail} {provider} {step.AccountName} {result.Receipt}".Trim(), null, cancellationToken);
        return true;
    }

    private static bool Matches(PersonAccount account, LifecycleStep step)
    {
        if (step.Code == "revoke-sso")
        {
            return account.Provider is "Okta" or "Google" or "Microsoft" or "Entra";
        }

        if (step.Code == "remove-github")
        {
            return string.Equals(account.Provider, "GitHub", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(account.Provider, step.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(account.AccountName, step.AccountName, StringComparison.Ordinal);
    }

    private static string ProviderFor(string code) => code switch
    {
        "remove-github" => "GitHub",
        _ => "SSO",
    };

    private async Task<Guid?> WriteEvidenceAsync(
        TenantDbContext db,
        LifecycleChecklist checklist,
        LifecycleStep step,
        string provider,
        AccessRevocationResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var controlItem = await db.Controls.FirstOrDefaultAsync(item => item.Code == LifecycleRules.OffboardingControl, cancellationToken);
        if (controlItem is null)
        {
            return null;
        }

        var retention = await control.Tenants.AsNoTracking().Where(item => item.Id == tenant.TenantId).Select(item => item.RetentionYears).FirstOrDefaultAsync(cancellationToken);
        retention = Math.Max(retention, DatabaseNames.MinimumRetentionYears);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "access_revocation_receipt_v1",
            person = checklist.PersonEmail,
            checklistId = checklist.Id,
            step = step.Code,
            provider,
            account = step.AccountName,
            mode = result.Mode,
            receipt = result.Receipt,
            detail = result.Detail,
            triggeredAt = checklist.TriggeredAt,
            accessDueAt = checklist.AccessDueAt,
            revokedAt = now,
            withinSla = checklist.AccessDueAt is null || now <= checklist.AccessDueAt,
            actor = tenant.Email,
        }, new JsonSerializerOptions { WriteIndented = true });
        var id = Guid.NewGuid();
        var account = step.AccountName.Length > 0 ? $" {step.AccountName}" : "";
        var item = new EvidenceItem
        {
            Id = id,
            ControlId = controlItem.Id,
            Title = Trim($"Access revoked: {checklist.PersonName} at {provider}{account}", 300),
            Source = "Personnel lifecycle",
            ObjectKey = $"{tenant.TenantId:N}/{id:N}.json",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(item);
        await evidence.AppendAsync(db, tenant.TenantId, item, payload, "application/json", now.AddYears(retention), cancellationToken);
        return id;
    }

    private void Mark(LifecycleStep step, bool done, string result)
    {
        step.Done = done;
        step.CompletedAt = done ? time.GetUtcNow() : null;
        step.CompletedBy = done ? Actor() : "";
        step.Result = Trim(result, 1000);
    }

    private static async Task SettleAsync(TenantDbContext db, LifecycleChecklist checklist, CancellationToken cancellationToken)
    {
        var access = checklist.Steps.Where(item => item.RevokesAccess).ToArray();
        checklist.AccessRevokedAt = access.Length > 0 && access.All(item => item.Done)
            ? access.Max(item => item.CompletedAt)
            : null;
        var complete = checklist.Steps.Count > 0 && checklist.Steps.All(item => item.Done);
        checklist.State = complete ? Complete : Open;
        checklist.CompletedAt = complete ? checklist.Steps.Max(item => item.CompletedAt) : null;
        if (complete && checklist.Kind == LifecycleKind.Offboarding)
        {
            var person = await db.People.FirstOrDefaultAsync(item => item.Id == checklist.PersonId, cancellationToken);
            if (person is not null)
            {
                person.State = PersonState.Offboarded;
            }
        }
    }

    private async Task<IReadOnlyList<LifecycleTestDto>> RefreshAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.LifecycleSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new LifecycleSettings();
        var now = time.GetUtcNow();
        var horizon = now.AddDays(-TestWindowDays);
        var rows = await db.LifecycleChecklists.AsNoTracking()
            .Where(item => item.TriggeredAt >= horizon || item.State == Open)
            .Where(item => item.TriggeredAt <= now)
            .Select(item => new ChecklistSlaSnapshot(item.PersonName, item.Kind, item.TriggeredAt, item.DueAt, item.AccessDueAt, item.AccessRevokedAt, item.CompletedAt))
            .ToListAsync(cancellationToken);
        var offboarding = LifecycleRules.EvaluateOffboarding(rows, now, settings.OffboardingAccessSlaHours);
        var onboarding = LifecycleRules.EvaluateOnboarding(rows, now, settings.OnboardingDays);
        await ApplyTestAsync(db, LifecycleRules.OffboardingControl, LifecycleRules.OffboardingTestName, offboarding, cancellationToken);
        await ApplyTestAsync(db, LifecycleRules.OnboardingControl, LifecycleRules.OnboardingTestName, onboarding, cancellationToken);
        return
        [
            new(LifecycleRules.OffboardingTestName, LifecycleRules.OffboardingControl, offboarding.Outcome.ToString(), offboarding.Detail),
            new(LifecycleRules.OnboardingTestName, LifecycleRules.OnboardingControl, onboarding.Outcome.ToString(), onboarding.Detail),
        ];
    }

    private async Task ApplyTestAsync(TenantDbContext db, string controlCode, string name, CheckResult result, CancellationToken cancellationToken)
    {
        var controlItem = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == controlCode, cancellationToken);
        if (controlItem is null)
        {
            return;
        }

        var test = controlItem.Tests.FirstOrDefault(item => item.Name == name);
        var previous = test?.Outcome;
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                Name = name,
                Kind = TestKind.Document,
            };
            controlItem.Tests.Add(test);
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = Trim(result.Detail, 1000);
        if (result.Outcome == TestOutcome.Fail)
        {
            controlItem.Status = ControlStatus.Failing;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (result.Outcome == TestOutcome.Fail && previous != TestOutcome.Fail)
        {
            await automation.DispatchAsync("TestFailed", name, result.Detail, cancellationToken);
            await automation.DispatchAsync("ControlNotReady", controlItem.Code, controlItem.Title, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<LifecycleTestDto>> TestsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var names = new[] { LifecycleRules.OffboardingTestName, LifecycleRules.OnboardingTestName };
        var rows = await db.ControlTests.AsNoTracking()
            .Where(item => names.Contains(item.Name))
            .Join(db.Controls.AsNoTracking(), test => test.ControlId, item => item.Id, (test, item) => new { test.Name, item.Code, test.Outcome, test.Detail })
            .ToListAsync(cancellationToken);
        return names.Select(name =>
        {
            var row = rows.FirstOrDefault(item => item.Name == name);
            var code = name == LifecycleRules.OffboardingTestName ? LifecycleRules.OffboardingControl : LifecycleRules.OnboardingControl;
            return row is null
                ? new LifecycleTestDto(name, code, "NotRun", "")
                : new LifecycleTestDto(name, row.Code, row.Outcome.ToString(), row.Detail ?? "");
        }).ToArray();
    }

    private async Task EnsureSeededAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        if (await db.LifecycleSettings.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        db.LifecycleSettings.Add(new LifecycleSettings { Id = Guid.NewGuid(), UpdatedAt = now });
        var order = 0;
        foreach (var template in LifecycleRules.DefaultTemplates)
        {
            db.LifecycleTemplateSteps.Add(new LifecycleTemplateStep
            {
                Id = Guid.NewGuid(),
                Kind = template.Kind,
                Code = template.Code,
                Title = template.Title,
                Category = template.Category,
                SlaHours = template.SlaHours,
                Order = order++,
                GroupCode = template.GroupCode,
                RevokesAccess = template.RevokesAccess,
                PerAccount = template.PerAccount,
            });
        }

        foreach (var group in LifecycleRules.DefaultGroups)
        {
            db.EmployeeGroups.Add(new EmployeeGroup
            {
                Id = Guid.NewGuid(),
                Code = group.Code,
                Name = group.Name,
                Description = group.Description,
                Departments = group.Departments,
                RoleKeywords = group.RoleKeywords,
                PolicyCodes = group.PolicyCodes,
                TrainingCodes = group.TrainingCodes,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        if (tenant.IsExample)
        {
            await SeedExampleAsync(db, now, cancellationToken);
        }
    }

    private async Task SeedExampleAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var emails = new[] { "sam.leaved@harborline.example", "winter.cruz@harborline.example", "dev.patel@harborline.example", "avery.abe@harborline.example" };
        var people = await db.People.Include(item => item.Accounts).Where(item => emails.Contains(item.Email)).ToListAsync(cancellationToken);
        var context = await GenerationContextAsync(db, cancellationToken);
        var sam = people.FirstOrDefault(item => item.Email == emails[0]);
        if (sam is not null)
        {
            if (!sam.Accounts.Any(item => item.Active))
            {
                sam.Accounts.Add(new PersonAccount { Id = Guid.NewGuid(), PersonId = sam.Id, Provider = "Slack", ExternalId = "U-SAM-LEAVED", AccountName = sam.Email, Active = true });
            }

            var overdue = Start(db, context, sam, LifecycleKind.Offboarding, "Hris", "Termination date from the HR system", now.AddDays(-3), DateOnly.FromDateTime(now.AddDays(-3).UtcDateTime));
            sam.State = PersonState.Offboarding;
            foreach (var step in overdue.Steps.Where(item => item.Code == "revoke-sso"))
            {
                step.Done = true;
                step.CompletedAt = overdue.TriggeredAt.AddHours(2);
                step.CompletedBy = "Lena Cho";
                step.Result = "Suspended in the identity provider";
            }
        }

        var winter = people.FirstOrDefault(item => item.Email == emails[1]);
        if (winter is not null)
        {
            var done = Start(db, context, winter, LifecycleKind.Offboarding, "Hris", "Termination date from the HR system", now.AddDays(-5), DateOnly.FromDateTime(now.AddDays(-5).UtcDateTime));
            foreach (var step in done.Steps)
            {
                step.Done = true;
                step.CompletedAt = done.TriggeredAt.AddHours(step.RevokesAccess ? 6 : 30);
                step.CompletedBy = "Lena Cho";
                step.Result = step.RevokesAccess ? "Access removed" : "Done";
            }

            done.AccessRevokedAt = done.TriggeredAt.AddHours(6);
            done.CompletedAt = done.TriggeredAt.AddHours(30);
            done.State = Complete;
            winter.State = PersonState.Offboarded;
        }

        var dev = people.FirstOrDefault(item => item.Email == emails[2]);
        if (dev is not null)
        {
            var mine = Start(db, context, dev, LifecycleKind.Onboarding, "Manual", "Moved to the platform team; engineering group requirements apply", now.AddDays(-4), DateOnly.FromDateTime(now.AddDays(-4).UtcDateTime));
            foreach (var step in mine.Steps.Where(item => item.Code is "enable-mfa" or "background-check"))
            {
                step.Done = true;
                step.CompletedAt = mine.TriggeredAt.AddHours(3);
                step.CompletedBy = "Lena Cho";
                step.Result = "Confirmed";
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(db, cancellationToken);
    }

    private async Task<PersonLifecycleDto> ToPersonAsync(TenantDbContext db, DirectoryPerson person, CancellationToken cancellationToken)
    {
        var groups = await db.EmployeeGroups.AsNoTracking().ToListAsync(cancellationToken);
        var mine = groups.Where(group => LifecycleRules.InGroup(group.Departments, group.RoleKeywords, person.Department, person.Role)).ToArray();
        var checklists = await db.LifecycleChecklists.AsNoTracking().Include(item => item.Steps)
            .Where(item => item.PersonId == person.Id)
            .OrderByDescending(item => item.TriggeredAt)
            .ToListAsync(cancellationToken);
        var codes = mine.Select(item => item.Code).ToArray();
        return new PersonLifecycleDto(
            person.Id,
            person.Name,
            person.Email,
            person.State.ToString(),
            person.HireOn,
            person.LeftOn,
            codes,
            mine.SelectMany(item => LifecycleRules.Split(item.PolicyCodes)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            mine.SelectMany(item => LifecycleRules.Split(item.TrainingCodes)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            checklists.Select(item => ToChecklist(item, codes)).ToArray());
    }

    private async Task<LifecycleChecklistDto> ChecklistDtoAsync(TenantDbContext db, LifecycleChecklist checklist, CancellationToken cancellationToken)
    {
        var groups = await db.EmployeeGroups.AsNoTracking().ToListAsync(cancellationToken);
        var lookup = await DepartmentsAsync(db, [checklist.PersonId], cancellationToken);
        return ToChecklist(checklist, GroupsOf(groups, lookup, checklist.PersonId));
    }

    private static async Task<Dictionary<Guid, (string Department, string Role)>> DepartmentsAsync(TenantDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        var rows = await db.People.AsNoTracking().Where(item => set.Contains(item.Id)).Select(item => new { item.Id, item.Department, item.Role }).ToListAsync(cancellationToken);
        return rows.ToDictionary(item => item.Id, item => (item.Department, item.Role));
    }

    private static IReadOnlyList<string> GroupsOf(IEnumerable<EmployeeGroup> groups, Dictionary<Guid, (string Department, string Role)> lookup, Guid personId) =>
        lookup.TryGetValue(personId, out var person)
            ? groups.Where(group => LifecycleRules.InGroup(group.Departments, group.RoleKeywords, person.Department, person.Role)).Select(group => group.Code).Order(StringComparer.Ordinal).ToArray()
            : [];

    private static IReadOnlyList<string> GroupsOf(IEnumerable<EmployeeGroup> groups, DirectoryPerson person) =>
        groups.Where(group => LifecycleRules.InGroup(group.Departments, group.RoleKeywords, person.Department, person.Role)).Select(group => group.Code).Order(StringComparer.Ordinal).ToArray();

    private LifecycleChecklistDto ToChecklist(LifecycleChecklist item, IReadOnlyList<string> groups)
    {
        var now = time.GetUtcNow();
        var steps = item.Steps.OrderBy(step => step.Order).ToArray();
        return new LifecycleChecklistDto(
            item.Id,
            item.PersonId,
            item.PersonName,
            item.PersonEmail,
            item.Kind.ToString(),
            item.Trigger,
            item.Reason,
            item.EffectiveOn,
            item.TriggeredAt,
            item.DueAt,
            item.AccessDueAt,
            item.AccessRevokedAt,
            item.CompletedAt,
            item.State,
            LifecycleRules.SlaState(item.DueAt, item.CompletedAt, now),
            item.AccessDueAt is DateTimeOffset accessDue ? LifecycleRules.SlaState(accessDue, item.AccessRevokedAt, now) : "",
            steps.Count(step => step.Done),
            steps.Length,
            groups,
            steps.Select(step => new LifecycleStepDto(
                step.Id,
                step.Code,
                step.Title,
                step.Category,
                step.Provider,
                step.AccountName,
                step.RevokesAccess,
                step.DueAt,
                step.Done,
                step.CompletedAt,
                step.CompletedBy,
                step.Result,
                step.EvidenceId,
                LifecycleRules.SlaState(step.DueAt, step.CompletedAt, now))).ToArray());
    }

    private static LifecycleSettingsDto ToSettings(LifecycleSettings item) =>
        new(item.OffboardingAccessSlaHours, item.OffboardingCompleteSlaHours, item.OnboardingDays);

    private static EmployeeGroupDto ToGroup(EmployeeGroup item, int members) =>
        new(item.Code, item.Name, item.Description, LifecycleRules.Split(item.Departments), LifecycleRules.Split(item.RoleKeywords), LifecycleRules.Split(item.PolicyCodes), LifecycleRules.Split(item.TrainingCodes), members);

    private static LifecycleTemplateStepDto ToTemplate(LifecycleTemplateStep item) =>
        new(item.Id, item.Kind.ToString(), item.Code, item.Title, item.Category, item.SlaHours, item.Order, item.GroupCode, item.RevokesAccess, item.PerAccount);

    private static string? Csv(IReadOnlyList<string>? values)
    {
        var items = (values ?? []).Select(item => (item ?? "").Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length > 50 || items.Any(item => item.Length > 64 || item.Contains(',')))
        {
            return null;
        }

        return string.Join(',', items);
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private static DateTimeOffset AtStartOfDay(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private string Actor() => string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Email : tenant.DisplayName;

    private TenantDbContext OpenDb() => databases.Create(tenant.DatabaseName);

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", Trim(detail, 500), null, cancellationToken);
}

public static class LifecycleSelfService
{
    public static bool CanComplete(string category) => category is "Policy" or "Training" or "Device" or "Identity";
}
