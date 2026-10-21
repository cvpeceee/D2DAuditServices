using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Policies;

public sealed class PolicyPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IPolicyPortal
{
    public async Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var controls = await LoadControlsAsync(db, cancellationToken);
        var policies = await db.Policies.AsNoTracking()
            .Include(item => item.Versions).ThenInclude(version => version.Acknowledgements)
            .Include(item => item.Controls)
            .ToListAsync(cancellationToken);
        return policies
            .Where(item => Visible(item, controls))
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .Select(item => ToListItem(item, controls))
            .ToArray();
    }

    public async Task<Result<PolicyDetail>> GetAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null)
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        var controls = await LoadControlsAsync(db, cancellationToken);
        return Visible(policy, controls)
            ? Result<PolicyDetail>.Ok(ToDetail(policy, controls))
            : Result<PolicyDetail>.Fail("not_found");
    }

    public async Task<Result<int>> InstallLibraryAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var existing = await db.Policies.Select(item => item.Code).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var added = 0;
        foreach (var template in PolicyTemplates.Core)
        {
            if (existing.Contains(template.Code, StringComparer.Ordinal))
            {
                continue;
            }

            var document = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                Code = template.Code,
                Title = template.Title,
                OwnerName = "Security owner",
            };
            document.Versions.Add(new PolicyVersion
            {
                Id = Guid.NewGuid(),
                Number = 1,
                Body = template.Body,
                State = PolicyState.Draft,
                CreatedAt = now,
            });
            foreach (var controlCode in template.ControlCodes)
            {
                document.Controls.Add(new PolicyControlLink { ControlCode = controlCode });
            }

            db.Policies.Add(document);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            await WriteAsync("policy.library.installed", added.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        }

        return Result<int>.Ok(added);
    }

    public async Task<Result<PolicyDetail>> DraftAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null || !Visible(policy, await LoadControlsAsync(db, cancellationToken)))
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        if (policy.Versions.Any(item => item.State is PolicyState.Draft or PolicyState.InReview))
        {
            return Result<PolicyDetail>.Fail("version_open");
        }

        var approved = policy.Versions.Where(item => item.State == PolicyState.Approved).OrderByDescending(item => item.Number).FirstOrDefault();
        if (approved is null)
        {
            return Result<PolicyDetail>.Fail("not_approved");
        }

        db.PolicyVersions.Add(new PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            Number = policy.Versions.Max(item => item.Number) + 1,
            Body = approved.Body,
            State = PolicyState.Draft,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.drafted", policy.Code, cancellationToken);
        return await GetAsync(code, cancellationToken);
    }

    public async Task<Result<PolicyDetail>> SubmitAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        var latest = policy?.Versions.OrderByDescending(item => item.Number).FirstOrDefault();
        if (policy is null || latest is null || !Visible(policy, await LoadControlsAsync(db, cancellationToken)))
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        var decision = PolicyWorkflow.Submit(latest.State);
        if (!decision.Succeeded)
        {
            return Result<PolicyDetail>.Fail(decision.Error ?? "not_draft");
        }

        latest.State = PolicyState.InReview;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.submitted", policy.Code, cancellationToken);
        return await GetAsync(code, cancellationToken);
    }

    public async Task<Result<PolicyDetail>> ApproveAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        var latest = policy?.Versions.OrderByDescending(item => item.Number).FirstOrDefault();
        if (policy is null || latest is null || !Visible(policy, await LoadControlsAsync(db, cancellationToken)))
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        var decision = PolicyWorkflow.Approve(latest.State);
        if (!decision.Succeeded)
        {
            return Result<PolicyDetail>.Fail(decision.Error ?? "not_in_review");
        }

        foreach (var version in policy.Versions.Where(item => item.State == PolicyState.Approved))
        {
            version.State = PolicyState.Superseded;
        }

        latest.State = PolicyState.Approved;
        latest.ApprovedAt = time.GetUtcNow();
        latest.ApprovedByName = tenant.DisplayName;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.approved", policy.Code, cancellationToken);
        return await GetAsync(code, cancellationToken);
    }

    public async Task<Result<PolicyDetail>> AssignAcknowledgementAsync(string code, AssignAcknowledgementRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PersonName) || request.PersonName.Length > 200
            || string.IsNullOrWhiteSpace(request.PersonEmail) || !request.PersonEmail.Contains('@') || request.PersonEmail.Length > 320)
        {
            return Result<PolicyDetail>.Fail("invalid_acknowledgement");
        }

        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        var approved = policy?.Versions.Where(item => item.State == PolicyState.Approved).OrderByDescending(item => item.Number).FirstOrDefault();
        if (policy is null || approved is null || !Visible(policy, await LoadControlsAsync(db, cancellationToken)))
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        var email = request.PersonEmail.Trim();
        if (approved.Acknowledgements.Any(item => string.Equals(item.PersonEmail, email, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<PolicyDetail>.Fail("already_assigned");
        }

        var acknowledgement = new PolicyAcknowledgement
        {
            Id = Guid.NewGuid(),
            PolicyVersionId = approved.Id,
            PersonName = request.PersonName.Trim(),
            PersonEmail = email,
            DueOn = request.DueOn,
        };
        db.Acknowledgements.Add(acknowledgement);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.acknowledgement.assigned", policy.Code, cancellationToken);
        return await GetAsync(code, cancellationToken);
    }

    public async Task<Result<PolicyDetail>> AcknowledgeAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null || !Visible(policy, await LoadControlsAsync(db, cancellationToken)))
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        var open = policy.Versions
            .SelectMany(version => version.Acknowledgements)
            .Where(item => item.AcknowledgedAt is null && string.Equals(item.PersonEmail, tenant.Email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.DueOn)
            .FirstOrDefault();
        if (open is null)
        {
            return Result<PolicyDetail>.Fail("not_found");
        }

        open.AcknowledgedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.acknowledged", policy.Code, cancellationToken);
        return await GetAsync(code, cancellationToken);
    }

    public async Task<Result<int>> RemindAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var controls = await LoadControlsAsync(db, cancellationToken);
        var policies = await db.Policies.Include(item => item.Controls).Include(item => item.Versions).ThenInclude(version => version.Acknowledgements).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var due = policies
            .Where(item => Visible(item, controls))
            .SelectMany(item => item.Versions)
            .SelectMany(version => version.Acknowledgements)
            .Where(item => AcknowledgementRules.NeedsReminder(item.DueOn, item.AcknowledgedAt, item.RemindedAt, today))
            .ToList();
        var now = time.GetUtcNow();
        foreach (var item in due)
        {
            item.RemindedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (due.Count > 0)
        {
            await WriteAsync("policy.reminded", due.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        }

        return Result<int>.Ok(due.Count);
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private static async Task<PolicyDocument?> FindAsync(TenantDbContext db, string code, CancellationToken cancellationToken) =>
        await db.Policies
            .Include(item => item.Controls)
            .Include(item => item.Versions).ThenInclude(version => version.Acknowledgements)
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);

    private static async Task<Dictionary<string, Control>> LoadControlsAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .ToListAsync(cancellationToken);
        return controls.ToDictionary(item => item.Code, StringComparer.Ordinal);
    }

    private bool Visible(PolicyDocument policy, IReadOnlyDictionary<string, Control> controls)
    {
        if (!tenant.IsAuditor)
        {
            return true;
        }

        return policy.Controls.Any(link => controls.TryGetValue(link.ControlCode, out var control) && InScope(control));
    }

    private bool InScope(Control control) =>
        control.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && tenant.AuditorFrameworkKeys.Contains(key, StringComparer.Ordinal));

    private PolicyListItem ToListItem(PolicyDocument policy, IReadOnlyDictionary<string, Control> controls)
    {
        var latest = policy.Versions.OrderByDescending(item => item.Number).First();
        var approved = policy.Versions.Where(item => item.State == PolicyState.Approved).OrderByDescending(item => item.Number).FirstOrDefault();
        var tracked = approved?.Acknowledgements ?? [];
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return new PolicyListItem(
            policy.Code,
            policy.Title,
            policy.OwnerName,
            latest.Number,
            latest.State.ToString(),
            Codes(policy, controls),
            tracked.Count(item => item.AcknowledgedAt is not null),
            tracked.Count(item => item.AcknowledgedAt is null && item.DueOn < today));
    }

    private PolicyDetail ToDetail(PolicyDocument policy, IReadOnlyDictionary<string, Control> controls)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var versions = policy.Versions.OrderByDescending(item => item.Number).Select(item => new PolicyVersionDto(
            item.Number,
            item.State.ToString(),
            item.Body,
            item.CreatedAt,
            item.ApprovedAt,
            item.ApprovedByName)).ToArray();
        var approved = policy.Versions.Where(item => item.State == PolicyState.Approved).OrderByDescending(item => item.Number).FirstOrDefault();
        var rows = (approved?.Acknowledgements ?? []).Select(item => new AcknowledgementDto(
            item.PersonName,
            item.PersonEmail,
            item.DueOn,
            item.AcknowledgedAt,
            item.RemindedAt,
            AcknowledgementRules.NeedsReminder(item.DueOn, item.AcknowledgedAt, item.RemindedAt, today))).ToArray();
        return new PolicyDetail(policy.Code, policy.Title, policy.OwnerName, Codes(policy, controls), versions, rows);
    }

    private IReadOnlyList<string> Codes(PolicyDocument policy, IReadOnlyDictionary<string, Control> controls) =>
        policy.Controls
            .Select(link => link.ControlCode)
            .Where(code => !tenant.IsAuditor || (controls.TryGetValue(code, out var control) && InScope(control)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
