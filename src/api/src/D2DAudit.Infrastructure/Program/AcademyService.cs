using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Program;

public sealed class AcademyService(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    TimeProvider time) : IAcademy
{
    private const string Unassigned = "None";

    public async Task<AcademyOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var context = await LoadAsync(db, cancellationToken);
        var today = Today();
        var items = context.Modules.Select(module => Item(module, context, today)).ToArray();
        var required = items.Where(item => item.Required).ToArray();
        return new AcademyOverview(items, required.Length, required.Count(item => item.Status == "Passed"), SecurityAcademy.PassMark, SecurityAcademy.RenewalMonths);
    }

    public async Task<Result<AcademyModuleDetail>> ModuleAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var context = await LoadAsync(db, cancellationToken);
        var module = context.Modules.FirstOrDefault(item => item.Key == key);
        if (module is null)
        {
            return Result<AcademyModuleDetail>.Fail("not_found");
        }

        return Result<AcademyModuleDetail>.Ok(new AcademyModuleDetail(
            Item(module, context, Today()),
            module.Sections.Select(item => new AcademySectionDto(item.Heading, item.Body)).ToArray(),
            module.Questions.Select(item => new AcademyQuestionDto(item.Prompt, item.Choices)).ToArray()));
    }

    public async Task<Result<AcademyModuleItem>> ProgressAsync(string key, AcademyProgressRequest request, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<AcademyModuleItem>.Fail("forbidden");
        }

        await using var db = Open();
        var context = await LoadAsync(db, cancellationToken);
        var module = context.Modules.FirstOrDefault(item => item.Key == key);
        if (module is null)
        {
            return Result<AcademyModuleItem>.Fail("not_found");
        }

        if (request.SectionsCompleted < 0 || request.SectionsCompleted > module.Sections.Count)
        {
            return Result<AcademyModuleItem>.Fail("invalid_progress");
        }

        var row = await RowAsync(db, module.Key, context.PersonName, cancellationToken);
        row.SectionsCompleted = Math.Max(row.SectionsCompleted, request.SectionsCompleted);
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        context.Progress[module.Key] = row;
        return Result<AcademyModuleItem>.Ok(Item(module, context, Today()));
    }

    public async Task<Result<AcademyQuizResult>> QuizAsync(string key, AcademyQuizRequest request, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<AcademyQuizResult>.Fail("forbidden");
        }

        await using var db = Open();
        var context = await LoadAsync(db, cancellationToken);
        var module = context.Modules.FirstOrDefault(item => item.Key == key);
        if (module is null)
        {
            return Result<AcademyQuizResult>.Fail("not_found");
        }

        if (request.Answers is null || request.Answers.Count != module.Questions.Count)
        {
            return Result<AcademyQuizResult>.Fail("invalid_answers");
        }

        var score = SecurityAcademy.Score(module, request.Answers);
        var row = await RowAsync(db, module.Key, context.PersonName, cancellationToken);
        var now = time.GetUtcNow();
        var today = Today();
        row.Attempts++;
        row.LastScore = score.Correct;
        row.UpdatedAt = now;
        var explanations = score.Wrong.Select(index => module.Questions[index].Explanation).ToArray();
        if (!score.Passed)
        {
            await db.SaveChangesAsync(cancellationToken);
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "academy.quiz", "failed", module.Key, null, cancellationToken);
            return Result<AcademyQuizResult>.Ok(new AcademyQuizResult(score.Correct, score.Total, SecurityAcademy.PassMark, false, score.Wrong, explanations, null, null, null));
        }

        var renewing = !SecurityAcademy.IsCurrent(row.ExpiresOn, today);
        row.BestScore = renewing ? score.Correct : Math.Max(row.BestScore, score.Correct);
        row.SectionsCompleted = module.Sections.Count;
        if (renewing)
        {
            row.PassedAt = now;
            row.ExpiresOn = SecurityAcademy.ExpiresOn(today);
            row.CertificateNumber = SecurityAcademy.CertificateNumber(tenant.TenantId, module.Key, tenant.Email, today);
            var body = string.Join('\n', SecurityAcademy.CertificateLines(tenant.TenantName, context.PersonName, module, row));
            var evidenceId = await StoreAsync(db, module.Title + " " + context.PersonName, body, cancellationToken);
            row.EvidenceId = evidenceId == Guid.Empty ? null : evidenceId;
        }

        await db.SaveChangesAsync(cancellationToken);
        context.Progress[module.Key] = row;
        await FeedTrainingAsync(db, context, today, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "academy.passed", "ok", module.Key, null, cancellationToken);
        return Result<AcademyQuizResult>.Ok(new AcademyQuizResult(score.Correct, score.Total, SecurityAcademy.PassMark, true, score.Wrong, explanations, row.ExpiresOn, row.CertificateNumber, row.EvidenceId));
    }

    public async Task<Result<FileExport>> CertificateAsync(string key, CancellationToken cancellationToken)
    {
        var module = SecurityAcademy.Find(key);
        if (module is null)
        {
            return Result<FileExport>.Fail("not_found");
        }

        await using var db = Open();
        var email = tenant.Email;
        var row = await db.AcademyProgress.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ModuleKey == key && EF.Functions.ILike(item.PersonEmail, email), cancellationToken);
        if (row?.PassedAt is null)
        {
            return Result<FileExport>.Fail("not_completed");
        }

        var name = string.IsNullOrWhiteSpace(row.PersonName) ? tenant.DisplayName : row.PersonName;
        var pdf = PlainPdf.FromLines(SecurityAcademy.CertificateLines(tenant.TenantName, name, module, row));
        return Result<FileExport>.Ok(new FileExport($"certificate-{module.Key}.pdf", "application/pdf", pdf));
    }

    public async Task<Result<IReadOnlyList<AcademyAssignmentItem>>> AssignmentsAsync(CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<IReadOnlyList<AcademyAssignmentItem>>.Fail("forbidden");
        }

        await using var db = Open();
        var frameworks = await db.Frameworks.AsNoTracking().Select(item => item.Key).ToListAsync(cancellationToken);
        var assignments = await db.AcademyAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking().Where(item => item.State == PersonState.Active).Select(item => new { item.Email, item.Department, item.Role }).ToListAsync(cancellationToken);
        var progress = await db.AcademyProgress.AsNoTracking().ToListAsync(cancellationToken);
        var today = Today();
        IReadOnlyList<AcademyAssignmentItem> items = SecurityAcademy.Modules.Select(module =>
        {
            var audiences = Audiences(module, assignments);
            var available = SecurityAcademy.Available(module, frameworks);
            var assigned = available ? people.Where(person => SecurityAcademy.IsAssigned(audiences, person.Department, person.Role)).ToArray() : [];
            var current = assigned.Count(person => progress.Any(row => row.ModuleKey == module.Key
                && string.Equals(row.PersonEmail, person.Email, StringComparison.OrdinalIgnoreCase)
                && SecurityAcademy.IsCurrent(row.ExpiresOn, today)));
            return new AcademyAssignmentItem(module.Key, module.Title, available, audiences, assigned.Length, current);
        }).ToArray();
        return Result<IReadOnlyList<AcademyAssignmentItem>>.Ok(items);
    }

    public async Task<Result<AcademyAssignmentItem>> SaveAssignmentAsync(string key, SaveAcademyAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<AcademyAssignmentItem>.Fail("forbidden");
        }

        var module = SecurityAcademy.Find(key);
        if (module is null)
        {
            return Result<AcademyAssignmentItem>.Fail("not_found");
        }

        var audiences = (request.Audiences ?? []).Select(item => (item ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (audiences.Length > 20 || audiences.Any(item => !SecurityAcademy.IsValidAudience(item)))
        {
            return Result<AcademyAssignmentItem>.Fail("invalid_audience");
        }

        await using var db = Open();
        var existing = await db.AcademyAssignments.Where(item => item.ModuleKey == key).ToListAsync(cancellationToken);
        db.AcademyAssignments.RemoveRange(existing);
        var now = time.GetUtcNow();
        foreach (var audience in audiences.Length == 0 ? [Unassigned] : audiences)
        {
            db.AcademyAssignments.Add(new AcademyAssignment { Id = Guid.NewGuid(), ModuleKey = key, Audience = audience, UpdatedAt = now, UpdatedByEmail = tenant.Email });
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "academy.assigned", "ok", key + " " + string.Join(", ", audiences), null, cancellationToken);
        var all = await AssignmentsAsync(cancellationToken);
        return Result<AcademyAssignmentItem>.Ok(all.Value!.First(item => item.ModuleKey == key));
    }

    private sealed record Context(
        IReadOnlyList<AcademyModuleContent> Modules,
        IReadOnlyList<string> Frameworks,
        IReadOnlyList<AcademyAssignment> Assignments,
        Dictionary<string, AcademyProgress> Progress,
        DirectoryPerson? Person,
        string PersonName);

    private async Task<Context> LoadAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var frameworks = await db.Frameworks.AsNoTracking().Select(item => item.Key).ToListAsync(cancellationToken);
        var assignments = await db.AcademyAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var email = tenant.Email;
        var person = await db.People.Include(item => item.Tasks).FirstOrDefaultAsync(item => EF.Functions.ILike(item.Email, email), cancellationToken);
        var progress = await db.AcademyProgress.AsNoTracking().Where(item => EF.Functions.ILike(item.PersonEmail, email)).ToListAsync(cancellationToken);
        var modules = SecurityAcademy.Modules.Where(module => SecurityAcademy.Available(module, frameworks)).ToArray();
        return new Context(
            modules,
            frameworks,
            assignments,
            progress.ToDictionary(item => item.ModuleKey, StringComparer.Ordinal),
            person,
            person?.Name ?? tenant.DisplayName);
    }

    private static IReadOnlyList<string> Audiences(AcademyModuleContent module, IEnumerable<AcademyAssignment> assignments)
    {
        var rows = assignments.Where(item => item.ModuleKey == module.Key).Select(item => item.Audience).ToArray();
        if (rows.Length == 0)
        {
            return module.DefaultAudiences;
        }

        return rows.Where(item => item != Unassigned).Order(StringComparer.Ordinal).ToArray();
    }

    private static AcademyModuleItem Item(AcademyModuleContent module, Context context, DateOnly today)
    {
        var audiences = Audiences(module, context.Assignments);
        var required = SecurityAcademy.IsAssigned(audiences, context.Person?.Department ?? "", context.Person?.Role ?? "");
        var row = context.Progress.GetValueOrDefault(module.Key);
        var status = SecurityAcademy.Status(row, today);
        return new AcademyModuleItem(
            module.Key,
            module.Title,
            module.Summary,
            module.Minutes,
            required,
            module.RequiresFramework is not null,
            audiences,
            module.Sections.Count,
            row?.SectionsCompleted ?? 0,
            status,
            row?.BestScore ?? 0,
            module.Questions.Count,
            row?.PassedAt,
            row?.ExpiresOn,
            row?.PassedAt is null ? null : row.CertificateNumber);
    }

    private async Task<AcademyProgress> RowAsync(TenantDbContext db, string key, string personName, CancellationToken cancellationToken)
    {
        var email = tenant.Email;
        var row = await db.AcademyProgress.FirstOrDefaultAsync(item => item.ModuleKey == key && EF.Functions.ILike(item.PersonEmail, email), cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new AcademyProgress { Id = Guid.NewGuid(), ModuleKey = key, PersonEmail = email, PersonName = personName, UpdatedAt = time.GetUtcNow() };
        db.AcademyProgress.Add(row);
        return row;
    }

    private static async Task FeedTrainingAsync(TenantDbContext db, Context context, DateOnly today, CancellationToken cancellationToken)
    {
        var person = context.Person;
        if (person is null)
        {
            return;
        }

        var required = context.Modules
            .Where(module => SecurityAcademy.IsAssigned(Audiences(module, context.Assignments), person.Department, person.Role))
            .Select(module => module.Key)
            .ToArray();
        if (!SecurityAcademy.AllCurrent(required, context.Progress.Values, today))
        {
            return;
        }

        var tracked = await db.People.Include(item => item.Tasks).FirstAsync(item => item.Id == person.Id, cancellationToken);
        tracked.Training = TrainingState.Complete;
        tracked.TrainingCompletedOn = today;
        var task = tracked.Tasks.FirstOrDefault(item => item.TaskCode == "training");
        if (task is not null)
        {
            task.Done = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        var people = await db.People.AsNoTracking().Select(item => new { item.State, item.TrainingCompletedOn }).ToListAsync(cancellationToken);
        var current = TrainingRules.OrganizationCurrent(people.Select(item => (item.State.ToString(), item.TrainingCompletedOn)), today);
        var controlRow = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "HR-06", cancellationToken);
        var test = controlRow?.Tests.FirstOrDefault(item => item.Name == "Personnel completed training within 12 months");
        if (controlRow is null || test is null)
        {
            return;
        }

        controlRow.Status = current ? ControlStatus.Passing : ControlStatus.Failing;
        test.Outcome = current ? TestOutcome.Pass : TestOutcome.Fail;
        test.Detail = current ? "Personnel completed training within 12 months." : "Some personnel are outside the 12-month training window.";
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> StoreAsync(TenantDbContext db, string title, string body, CancellationToken cancellationToken)
    {
        var controlId = await db.Controls.Where(item => item.Code == "HR-06").Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
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
            Source = "Security academy",
            ObjectKey = tenant.TenantId.ToString("N") + "/" + evidenceId.ToString("N") + ".txt",
            Integrity = EvidenceIntegrity.NotStored,
        };
        db.Evidence.Add(stored);
        await evidence.AppendAsync(db, tenant.TenantId, stored, Encoding.UTF8.GetBytes(body), "text/plain", time.GetUtcNow().AddYears(years), cancellationToken);
        return evidenceId;
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);
}
