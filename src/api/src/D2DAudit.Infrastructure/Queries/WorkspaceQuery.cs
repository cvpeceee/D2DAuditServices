using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Queries;

public sealed class WorkspaceQuery(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IActivityWriter activity,
    IValidator<AssignOwnerRequest> owners,
    IGovernance governance) : IWorkspaceQuery
{
    public async Task<WorkspaceResponse> GetAsync(CancellationToken cancellationToken)
    {
        var controls = await LoadControlsAsync(cancellationToken);
        return new WorkspaceResponse(
            tenant.TenantId,
            tenant.TenantName,
            tenant.Region,
            tenant.Role.ToString(),
            tenant.IsExample,
            tenant.CanWrite,
            tenant.DisplayName,
            tenant.Email,
            controls.Count(controlItem => controlItem.Status == ControlStatus.Failing));
    }

    public async Task<DashboardResponse> DashboardAsync(CancellationToken cancellationToken)
    {
        var controls = await LoadControlsAsync(cancellationToken);
        var frameworks = FrameworkRollup(controls);
        var passing = controls.Count(controlItem => controlItem.Status == ControlStatus.Passing);
        var health = new ControlHealth(
            passing,
            controls.Count(controlItem => controlItem.Status == ControlStatus.Failing),
            controls.Count(controlItem => controlItem.Status == ControlStatus.DueSoon),
            controls.Count(controlItem => controlItem.Status == ControlStatus.NeedsEvidence),
            controls.Count(controlItem => controlItem.Status == ControlStatus.Manual),
            controls.Count(controlItem => controlItem.Status == ControlStatus.NotStarted));
        var failingTests = controls
            .SelectMany(controlItem => controlItem.Tests
                .Where(test => test.Outcome == TestOutcome.Fail)
                .Select(test => new FailingTestRow(
                    test.Id,
                    test.Name,
                    controlItem.Code,
                    test.Detail,
                    RequirementCodes(controlItem))))
            .OrderBy(row => row.ControlCode, StringComparer.Ordinal)
            .ToArray();

        await using var db = databases.Create(RequiredDatabase());
        var audits = await db.Audits.AsNoTracking().ToListAsync(cancellationToken);
        var audit = audits
            .Where(item => !tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(item.FrameworkKey, StringComparer.Ordinal))
            .OrderBy(item => item.FieldworkOn)
            .FirstOrDefault();
        AuditSummary? summary = null;
        if (audit is not null)
        {
            var open = controls.Count(controlItem => controlItem.Status is ControlStatus.Failing or ControlStatus.DueSoon or ControlStatus.NeedsEvidence);
            var samples = controls.SelectMany(controlItem => controlItem.Tests)
                .Count(test => test.Kind == TestKind.ManualSampling && test.Outcome != TestOutcome.Pass);
            summary = new AuditSummary(audit.Name, audit.FirmName, audit.FrameworkKey, audit.FieldworkOn, open, samples);
        }

        return new DashboardResponse(
            tenant.DisplayName,
            tenant.TenantName,
            tenant.Region,
            tenant.IsExample,
            Percent(passing, controls.Count),
            frameworks.Count,
            frameworks,
            summary,
            failingTests,
            health,
            await governance.DueItemsAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<FrameworkListItem>> FrameworksAsync(CancellationToken cancellationToken)
    {
        var controls = await LoadControlsAsync(cancellationToken);
        await using var db = databases.Create(RequiredDatabase());
        var frameworks = await db.Frameworks.AsNoTracking().Include(framework => framework.Requirements).ToListAsync(cancellationToken);
        return frameworks
            .Where(framework => !tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(framework.Key, StringComparer.Ordinal))
            .OrderBy(framework => framework.Name, StringComparer.Ordinal)
            .Select(framework =>
            {
                var mapped = controls.Where(controlItem => MapsTo(controlItem, framework.Key)).ToArray();
                var passing = mapped.Count(controlItem => controlItem.Status == ControlStatus.Passing);
                var leaves = framework.Requirements.Count(requirement => framework.Requirements.All(other => other.ParentId != requirement.Id));
                return new FrameworkListItem(
                    framework.Key,
                    framework.Name,
                    framework.Version,
                    framework.Description,
                    leaves,
                    mapped.Length,
                    passing,
                    Percent(passing, mapped.Length));
            })
            .ToArray();
    }

    public async Task<FrameworkDetail?> FrameworkAsync(string key, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor && !tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal))
        {
            return null;
        }

        await using var db = databases.Create(RequiredDatabase());
        var framework = await db.Frameworks.AsNoTracking().FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (framework is null)
        {
            return null;
        }

        var requirements = await db.Requirements.AsNoTracking()
            .Where(requirement => requirement.FrameworkId == framework.Id)
            .OrderBy(requirement => requirement.SortOrder)
            .ToListAsync(cancellationToken);
        var mappings = await db.ControlMappings.AsNoTracking()
            .Include(mapping => mapping.Control)
            .Include(mapping => mapping.Requirement)
            .Where(mapping => mapping.Requirement!.FrameworkId == framework.Id)
            .ToListAsync(cancellationToken);

        RequirementNodeDto Node(Requirement requirement)
        {
            var controls = mappings
                .Where(mapping => mapping.RequirementId == requirement.Id && mapping.Control is not null)
                .Select(mapping => new MappedControlDto(
                    mapping.Control!.Code,
                    mapping.Control.Title,
                    ApiName(mapping.Coverage),
                    ApiName(mapping.Control.Status)))
                .OrderBy(controlItem => controlItem.Code, StringComparer.Ordinal)
                .ToArray();
            var children = requirements
                .Where(child => child.ParentId == requirement.Id)
                .OrderBy(child => child.SortOrder)
                .Select(Node)
                .ToArray();
            return new RequirementNodeDto(requirement.Code, requirement.Title, requirement.Summary, controls, children);
        }

        var roots = requirements.Where(requirement => requirement.ParentId is null).OrderBy(requirement => requirement.SortOrder).Select(Node).ToArray();
        return new FrameworkDetail(framework.Key, framework.Name, framework.Version, framework.Description, roots);
    }

    public async Task<IReadOnlyList<ControlListItem>> ControlsAsync(
        string? status,
        string? query,
        string? ownerEmail,
        CancellationToken cancellationToken)
    {
        var controls = await LoadControlsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ControlStatus>(status, ignoreCase: true, out var parsed))
            {
                return [];
            }

            controls = controls.Where(controlItem => controlItem.Status == parsed).ToList();
        }

        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            var owner = ownerEmail.Trim().ToLowerInvariant();
            controls = controls.Where(controlItem => string.Equals(controlItem.OwnerEmail, owner, StringComparison.Ordinal)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            controls = controls.Where(controlItem =>
                controlItem.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || controlItem.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return controls
            .OrderBy(controlItem => controlItem.Code, StringComparer.Ordinal)
            .Select(controlItem => new ControlListItem(
                controlItem.Code,
                controlItem.Title,
                ApiName(controlItem.Status),
                controlItem.OwnerName,
                controlItem.OwnerEmail,
                controlItem.Frequency,
                controlItem.Tests.Count(test => test.Outcome == TestOutcome.Pass),
                controlItem.Tests.Count,
                FrameworkKeys(controlItem)))
            .ToArray();
    }

    public async Task<ControlDetail?> ControlAsync(string code, CancellationToken cancellationToken)
    {
        var controls = await LoadControlsAsync(cancellationToken);
        var controlItem = controls.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        return controlItem is null ? null : ToDetail(controlItem);
    }

    public async Task<Result<ControlDetail>> AssignOwnerAsync(string code, AssignOwnerRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "write_denied", "denied", tenant.Role.ToString(), null, cancellationToken);
            return Result<ControlDetail>.Fail("forbidden");
        }

        if (!(await owners.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return Result<ControlDetail>.Fail("invalid_owner");
        }

        var ownerName = request.OwnerName?.Trim();
        var ownerEmail = request.OwnerEmail?.Trim().ToLowerInvariant();

        await using var db = databases.Create(RequiredDatabase());
        var controlItem = await db.Controls
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .Include(item => item.Tests)
            .Include(item => item.Evidence)
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (controlItem is null || !InScope(controlItem))
        {
            return Result<ControlDetail>.Fail("not_found");
        }

        controlItem.OwnerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName;
        controlItem.OwnerEmail = string.IsNullOrWhiteSpace(ownerEmail) ? null : ownerEmail;
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "control_owner_changed", "allowed", controlItem.Code, null, cancellationToken);
        return Result<ControlDetail>.Ok(ToDetail(controlItem));
    }

    public async Task<IReadOnlyList<JobDto>> JobsAsync(CancellationToken cancellationToken)
    {
        var jobs = await control.Jobs.AsNoTracking()
            .Where(job => job.TenantId == tenant.TenantId)
            .OrderByDescending(job => job.UpdatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        return jobs.Select(job => new JobDto(
            job.Id,
            job.Kind,
            ApiName(job.Status),
            job.Step,
            job.TotalSteps,
            job.CurrentStep,
            job.Percent,
            job.Error,
            job.UpdatedAt)).ToArray();
    }

    public async Task<Result<IReadOnlyList<ActivityDto>>> ActivityAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<IReadOnlyList<ActivityDto>>.Fail("forbidden");
        }

        var events = await control.Activity.AsNoTracking()
            .Where(item => item.TenantId == tenant.TenantId)
            .OrderByDescending(item => item.At)
            .Take(100)
            .ToListAsync(cancellationToken);
        var actorIds = events.Where(item => item.ActorUserId is not null).Select(item => item.ActorUserId!.Value).Distinct().ToArray();
        var actors = await control.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
        return Result<IReadOnlyList<ActivityDto>>.Ok(events.Select(item => new ActivityDto(
            item.At,
            item.Action,
            item.Result,
            item.Detail,
            item.ActorUserId is Guid actor && actors.TryGetValue(actor, out var email) ? email : null,
            item.IpAddress)).ToArray());
    }

    private async Task<List<Control>> LoadControlsAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(RequiredDatabase());
        var controls = await db.Controls.AsNoTracking()
            .Include(controlItem => controlItem.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .Include(controlItem => controlItem.Tests)
            .Include(controlItem => controlItem.Evidence)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return controls.Where(InScope).ToList();
    }

    private bool InScope(Control controlItem)
    {
        if (!tenant.IsAuditor)
        {
            return true;
        }

        return controlItem.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal));
    }

    private static bool MapsTo(Control controlItem, string frameworkKey) =>
        controlItem.Mappings.Any(mapping => mapping.Requirement?.Framework?.Key == frameworkKey);

    private List<string> FrameworkKeys(Control controlItem) =>
        controlItem.Mappings
            .Select(mapping => mapping.Requirement?.Framework?.Key)
            .Where(key => key is not null && (!tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal)))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private List<string> RequirementCodes(Control controlItem) =>
        controlItem.Mappings
            .Where(mapping => mapping.Requirement is not null && (!tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(mapping.Requirement.Framework?.Key ?? "", StringComparer.Ordinal)))
            .Select(mapping => mapping.Requirement!.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private IReadOnlyList<FrameworkReadiness> FrameworkRollup(IReadOnlyList<Control> controls)
    {
        var keys = controls
            .SelectMany(FrameworkKeys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return keys.Select(key =>
        {
            var mapped = controls.Where(controlItem => MapsTo(controlItem, key)).ToArray();
            var passing = mapped.Count(controlItem => controlItem.Status == ControlStatus.Passing);
            var name = mapped.SelectMany(controlItem => controlItem.Mappings)
                .Select(mapping => mapping.Requirement?.Framework)
                .FirstOrDefault(framework => framework?.Key == key)?.Name ?? key;
            return new FrameworkReadiness(key, name, passing, mapped.Length, Percent(passing, mapped.Length));
        }).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    private ControlDetail ToDetail(Control controlItem)
    {
        var mappings = controlItem.Mappings
            .Where(mapping => mapping.Requirement?.Framework is not null)
            .Where(mapping => !tenant.IsAuditor || tenant.AuditorFrameworkKeys.Contains(mapping.Requirement!.Framework!.Key, StringComparer.Ordinal))
            .OrderBy(mapping => mapping.Requirement!.Code, StringComparer.Ordinal)
            .Select(mapping => new ControlMappingDto(
                mapping.Requirement!.Framework!.Key,
                mapping.Requirement.Framework.Name,
                mapping.Requirement.Code,
                mapping.Requirement.Title,
                ApiName(mapping.Coverage)))
            .ToArray();
        var tests = controlItem.Tests
            .OrderBy(test => test.Name, StringComparer.Ordinal)
            .Select(test => new ControlTestDto(test.Id, test.Name, ApiName(test.Kind), ApiName(test.Outcome), test.Detail, test.Remediation))
            .ToArray();
        var evidence = controlItem.Evidence
            .OrderByDescending(item => item.CollectedAt)
            .Select(item => new EvidenceDto(item.Id, item.Title, item.Source, item.CollectedAt, ApiName(item.Integrity), item.Sha256, item.LockedUntil))
            .ToArray();
        return new ControlDetail(
            controlItem.Code,
            controlItem.Title,
            controlItem.Description,
            ApiName(controlItem.Status),
            controlItem.OwnerName,
            controlItem.OwnerEmail,
            controlItem.Frequency,
            mappings,
            tests,
            evidence);
    }

    private string RequiredDatabase()
    {
        if (!tenant.IsResolved || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return tenant.DatabaseName;
    }

    private static int Percent(int passing, int total) =>
        total == 0 ? 0 : (int)Math.Round(passing * 100d / total, MidpointRounding.AwayFromZero);

    private static string ApiName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
