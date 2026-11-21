using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

public sealed class PolicyInsightPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IPolicyInsightPortal
{
    private const int DefaultWindowDays = 30;

    public async Task<Result<PolicyDiffDto>> DiffAsync(string code, int from, int to, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null)
        {
            return Result<PolicyDiffDto>.Fail("not_found");
        }

        var before = policy.Versions.FirstOrDefault(item => item.Number == from);
        var after = policy.Versions.FirstOrDefault(item => item.Number == to);
        if (before is null || after is null)
        {
            return Result<PolicyDiffDto>.Fail("not_found");
        }

        var lines = LineDiff.Compute(before.Body, after.Body);
        return Result<PolicyDiffDto>.Ok(new PolicyDiffDto(
            policy.Code,
            from,
            to,
            lines.Count(item => item.Kind == "added"),
            lines.Count(item => item.Kind == "removed"),
            lines.Select(item => new PolicyDiffLineDto(item.Kind, item.Text, item.OldNumber, item.NewNumber)).ToArray()));
    }

    public async Task<Result<IReadOnlyList<AttestationCampaignDto>>> AttestationsAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null)
        {
            return Result<IReadOnlyList<AttestationCampaignDto>>.Fail("not_found");
        }

        var rows = await Campaigns(db).AsNoTracking().Where(item => item.PolicyCode == policy.Code).ToListAsync(cancellationToken);
        var today = Today();
        return Result<IReadOnlyList<AttestationCampaignDto>>.Ok(rows.Select(item => ToDto(item, today)).ToArray());
    }

    public async Task<Result<AttestationCampaignDto>> StartAttestationAsync(string code, StartAttestationRequest request, CancellationToken cancellationToken)
    {
        var today = Today();
        var rule = AttestationRules.Validate(request.Group, request.DueOn, today);
        if (!rule.Succeeded)
        {
            return Result<AttestationCampaignDto>.Fail(rule.Error!);
        }

        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null)
        {
            return Result<AttestationCampaignDto>.Fail("not_found");
        }

        var result = await CreateCampaignAsync(db, policy, request.Group.Trim(), today, request.DueOn, cancellationToken);
        if (result.IsSuccess)
        {
            await WriteAsync("policy.attestation.started", $"{policy.Code} {request.Group.Trim()}", cancellationToken);
        }

        return result;
    }

    public async Task<Result<AttestationCampaignDto>> RenewAttestationAsync(string code, Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        var campaign = await db.AttestationCampaigns.FirstOrDefaultAsync(item => item.Id == id && item.PolicyCode == code, cancellationToken);
        if (policy is null || campaign is null)
        {
            return Result<AttestationCampaignDto>.Fail("not_found");
        }

        if (campaign.State == AttestationRules.Renewed)
        {
            return Result<AttestationCampaignDto>.Fail("already_renewed");
        }

        var today = Today();
        var result = await CreateCampaignAsync(db, policy, campaign.Group, today, today.AddDays(DefaultWindowDays), cancellationToken, campaign);
        if (result.IsSuccess)
        {
            await WriteAsync("policy.attestation.renewed", $"{policy.Code} {campaign.Group}", cancellationToken);
        }

        return result;
    }

    public async Task<Result<AttestationCampaignDto>> AttestAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var policy = await FindAsync(db, code, cancellationToken);
        if (policy is null)
        {
            return Result<AttestationCampaignDto>.Fail("not_found");
        }

        var campaigns = await Campaigns(db).Where(item => item.PolicyCode == policy.Code && item.State == AttestationRules.Active).ToListAsync(cancellationToken);
        var match = campaigns
            .SelectMany(item => item.Attestations.Select(row => (Campaign: item, Row: row)))
            .FirstOrDefault(pair => pair.Row.AttestedAt is null && string.Equals(pair.Row.PersonEmail, tenant.Email, StringComparison.OrdinalIgnoreCase));
        if (match.Row is null)
        {
            return Result<AttestationCampaignDto>.Fail("not_found");
        }

        match.Row.AttestedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("policy.attested", $"{policy.Code} v{match.Campaign.VersionNumber}", cancellationToken);
        return Result<AttestationCampaignDto>.Ok(ToDto(match.Campaign, Today()));
    }

    private async Task<Result<AttestationCampaignDto>> CreateCampaignAsync(
        TenantDbContext db,
        PolicyDocument policy,
        string group,
        DateOnly today,
        DateOnly dueOn,
        CancellationToken cancellationToken,
        PolicyAttestationCampaign? previous = null)
    {
        var approved = policy.Versions.Where(item => item.State == PolicyState.Approved).OrderByDescending(item => item.Number).FirstOrDefault();
        if (approved is null)
        {
            return Result<AttestationCampaignDto>.Fail("no_approved_version");
        }

        var people = (await db.People.AsNoTracking().Where(item => item.State == PersonState.Active).ToListAsync(cancellationToken))
            .Where(item => AttestationRules.InGroup(group, item.Department))
            .ToArray();
        if (people.Length == 0)
        {
            return Result<AttestationCampaignDto>.Fail("empty_group");
        }

        var campaign = new PolicyAttestationCampaign
        {
            Id = Guid.NewGuid(),
            PolicyCode = policy.Code,
            VersionNumber = approved.Number,
            Group = group,
            StartedOn = today,
            DueOn = dueOn,
            RenewOn = AttestationRules.RenewOn(today),
            State = AttestationRules.Active,
            CreatedByName = AssuranceScope.Actor(tenant),
            CreatedAt = time.GetUtcNow(),
            Attestations =
            [
                .. people
                    .GroupBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new PolicyAttestation { Id = Guid.NewGuid(), PersonName = item.First().Name, PersonEmail = item.Key }),
            ],
        };
        if (previous is not null)
        {
            previous.State = AttestationRules.Renewed;
        }

        db.AttestationCampaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        return Result<AttestationCampaignDto>.Ok(ToDto(campaign, today));
    }

    private async Task<PolicyDocument?> FindAsync(TenantDbContext db, string code, CancellationToken cancellationToken)
    {
        var policy = await db.Policies.AsNoTracking()
            .Include(item => item.Controls)
            .Include(item => item.Versions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (policy is null || !tenant.IsAuditor)
        {
            return policy;
        }

        var controls = await AssuranceScope.ControlsAsync(db, cancellationToken);
        return policy.Controls.Any(link => controls.TryGetValue(link.ControlCode, out var item) && AssuranceScope.InScope(tenant, item)) ? policy : null;
    }

    private static IQueryable<PolicyAttestationCampaign> Campaigns(TenantDbContext db) =>
        db.AttestationCampaigns.Include(item => item.Attestations).OrderByDescending(item => item.StartedOn).ThenByDescending(item => item.CreatedAt);

    private static AttestationCampaignDto ToDto(PolicyAttestationCampaign item, DateOnly today) => new(
        item.Id,
        item.PolicyCode,
        item.VersionNumber,
        item.Group,
        item.StartedOn,
        item.DueOn,
        item.RenewOn,
        AttestationRules.Status(item.State, item.RenewOn, today),
        item.Attestations.Count(row => row.AttestedAt is not null),
        item.Attestations.Count,
        item.Attestations.OrderBy(row => row.PersonName, StringComparer.Ordinal).Select(row => new AttestationPersonDto(row.PersonName, row.PersonEmail, row.AttestedAt)).ToArray());

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private TenantDbContext Open() => databases.Create(AssuranceScope.Database(tenant));

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
