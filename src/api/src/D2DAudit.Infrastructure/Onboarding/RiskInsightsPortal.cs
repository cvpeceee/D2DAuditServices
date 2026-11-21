using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Governance;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Onboarding;

public sealed class RiskInsightsPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IRiskInsights
{
    public async Task<RiskHeatmapResult> HeatmapAsync(CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName);
        var rows = await db.Risks.AsNoTracking()
            .Select(item => new { item.Code, item.Likelihood, item.Impact, item.ResidualLikelihood, item.ResidualImpact })
            .ToListAsync(cancellationToken);
        return RiskHeatmap.Build(rows.Select(item => (item.Code, item.Likelihood, item.Impact, item.ResidualLikelihood, item.ResidualImpact)));
    }

    public async Task<Result<RiskListItem>> AcceptAsync(string code, AcceptRiskRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<RiskListItem>.Fail("forbidden");
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var approver = string.IsNullOrWhiteSpace(request.Approver) ? tenant.DisplayName : request.Approver.Trim();
        if (request.ExpiresOn <= today || request.ExpiresOn > today.AddYears(1) || approver.Length > 200 || request.Rationale is { Length: > 2000 })
        {
            return Result<RiskListItem>.Fail("invalid_acceptance");
        }

        await using var db = databases.Create(tenant.DatabaseName);
        var row = await db.Risks.Include(item => item.Controls).FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (row is null)
        {
            return Result<RiskListItem>.Fail("not_found");
        }

        row.Treatment = RiskTreatment.Accept;
        row.AcceptedBy = approver;
        row.AcceptedOn = today;
        row.AcceptanceExpiresOn = request.ExpiresOn;
        if (!string.IsNullOrWhiteSpace(request.Rationale))
        {
            row.TreatmentPlan = request.Rationale.Trim();
        }

        if (row.ReviewOn > request.ExpiresOn || row.ReviewOn < today)
        {
            row.ReviewOn = request.ExpiresOn;
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "risk.accepted", "ok", $"{row.Code} until {request.ExpiresOn:yyyy-MM-dd}", null, cancellationToken);
        return Result<RiskListItem>.Ok(GovernancePortal.ToRisk(row));
    }
}
