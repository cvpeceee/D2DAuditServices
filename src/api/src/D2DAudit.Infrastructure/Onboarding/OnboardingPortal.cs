using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Onboarding;

public sealed class OnboardingPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IFrameworkCatalog catalog,
    IFeatureGate gate,
    IActivityWriter activity,
    TimeProvider time) : IOnboarding
{
    public const string ReadinessKind = "Readiness";

    public async Task<OnboardingStateDto> GetAsync(CancellationToken cancellationToken) =>
        ToState(await LoadAsync(tracked: false, cancellationToken));

    public async Task<Result<OnboardingStateDto>> SaveAsync(SaveOnboardingRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<OnboardingStateDto>.Fail("forbidden");
        }

        if (request.CompanySize is not null && !FrameworkRecommender.Sizes.Contains(request.CompanySize)
            || request.Industry is not null && !FrameworkRecommender.Industries.Contains(request.Industry)
            || request.CloudProviders?.Any(item => !FrameworkRecommender.CloudOptions.Contains(item)) == true
            || request.DataTypes?.Any(item => !FrameworkRecommender.DataTypeOptions.Contains(item)) == true
            || request.Frameworks?.Any(item => catalog.Frameworks.All(framework => framework.Key != item)) == true
            || request.ScopedControls?.Any(item => item.Length is 0 or > 32) == true
            || request.ScopedControls?.Count > 400
            || request.CompleteStep is not null && !OnboardingSteps.All.Contains(request.CompleteStep))
        {
            return Result<OnboardingStateDto>.Fail("invalid_onboarding");
        }

        var row = await LoadAsync(tracked: true, cancellationToken);
        row.CompanySize = request.CompanySize ?? row.CompanySize;
        row.Industry = request.Industry ?? row.Industry;
        if (request.CloudProviders is not null)
        {
            row.CloudProviders = OnboardingSteps.Join(request.CloudProviders);
        }

        if (request.DataTypes is not null)
        {
            row.DataTypes = OnboardingSteps.Join(request.DataTypes);
        }

        if (request.ScopedControls is not null)
        {
            row.ScopedControls = OnboardingSteps.Join(request.ScopedControls);
        }

        if (request.Frameworks is not null)
        {
            row.Frameworks = OnboardingSteps.Join(request.Frameworks);
            var entitlements = await gate.EntitlementsAsync(tenant.TenantId, cancellationToken);
            var decision = FeatureGate.Check(entitlements, PlanFeatures.Frameworks, Split(row.Frameworks).Count);
            if (decision.OverLimit)
            {
                await activity.WriteAsync(tenant.UserId, tenant.TenantId, "plan.limit_exceeded", "warning", $"frameworks {decision.Used}/{decision.Limit}", null, cancellationToken);
            }
        }

        if (request.CompleteStep is not null)
        {
            row.CompletedSteps = OnboardingSteps.Join(OnboardingSteps.Parse(row.CompletedSteps).Append(request.CompleteStep));
        }

        row.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "onboarding.saved", "ok", request.CompleteStep, null, cancellationToken);
        return Result<OnboardingStateDto>.Ok(ToState(row));
    }

    public RecommendationResult Recommend(RecommendFrameworksRequest request) =>
        FrameworkRecommender.Recommend(new CompanyProfile(
            request.CompanySize ?? "",
            request.Industry ?? "",
            (request.CloudProviders ?? []).Where(FrameworkRecommender.CloudOptions.Contains).ToArray(),
            (request.DataTypes ?? []).Where(FrameworkRecommender.DataTypeOptions.Contains).ToArray(),
            request.International,
            request.UsesAi));

    public async Task<Result<ReadinessPlanDto>> GeneratePlanAsync(GenerateReadinessPlanRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ReadinessPlanDto>.Fail("forbidden");
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (ReadinessPlan.TooSoon(today, request.TargetDate))
        {
            return Result<ReadinessPlanDto>.Fail("target_too_soon");
        }

        if (request.TargetDate > today.AddYears(3))
        {
            return Result<ReadinessPlanDto>.Fail("target_too_far");
        }

        var row = await LoadAsync(tracked: true, cancellationToken);
        var frameworks = Split(row.Frameworks);
        var milestones = ReadinessPlan.Generate(today, request.TargetDate, frameworks);

        await using (var db = databases.Create(tenant.DatabaseName))
        {
            var previous = await db.WorkTasks.Where(item => item.LinkedKind == ReadinessKind && item.Status == "Open").ToListAsync(cancellationToken);
            db.WorkTasks.RemoveRange(previous);
            foreach (var milestone in milestones)
            {
                foreach (var task in milestone.Tasks)
                {
                    db.WorkTasks.Add(new WorkTask
                    {
                        Id = Guid.NewGuid(),
                        Title = task.Title,
                        LinkedKind = ReadinessKind,
                        LinkedCode = milestone.Key,
                        OwnerName = tenant.DisplayName,
                        OwnerEmail = tenant.Email,
                        DueOn = task.DueOn,
                        Status = "Open",
                        Recurrence = "None",
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        row.AuditTargetDate = request.TargetDate;
        row.CompletedSteps = OnboardingSteps.Join(OnboardingSteps.Parse(row.CompletedSteps).Append("plan"));
        row.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);

        var entitlements = await gate.EntitlementsAsync(tenant.TenantId, cancellationToken);
        var decision = FeatureGate.Check(entitlements, PlanFeatures.Frameworks, frameworks.Count);
        var created = milestones.Sum(item => item.Tasks.Count);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "onboarding.plan_generated", "ok", $"{request.TargetDate:yyyy-MM-dd} tasks={created}", null, cancellationToken);
        return Result<ReadinessPlanDto>.Ok(new ReadinessPlanDto(request.TargetDate, milestones, created, decision));
    }

    public async Task<OnboardingStateDto> DismissAsync(CancellationToken cancellationToken)
    {
        var row = await LoadAsync(tracked: true, cancellationToken);
        row.Dismissed = true;
        row.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        return ToState(row);
    }

    internal static IReadOnlyList<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<TenantOnboarding> LoadAsync(bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? control.TenantOnboarding : control.TenantOnboarding.AsNoTracking();
        var row = await query.FirstOrDefaultAsync(item => item.TenantId == tenant.TenantId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new TenantOnboarding { TenantId = tenant.TenantId, UpdatedAt = time.GetUtcNow() };
        if (tracked)
        {
            control.TenantOnboarding.Add(row);
        }

        return row;
    }

    private OnboardingStateDto ToState(TenantOnboarding row)
    {
        var completed = OnboardingSteps.Parse(row.CompletedSteps);
        var percent = (int)Math.Round(100.0 * completed.Count / OnboardingSteps.All.Count);
        var options = new OnboardingOptionsDto(
            FrameworkRecommender.Sizes,
            FrameworkRecommender.Industries,
            FrameworkRecommender.CloudOptions,
            FrameworkRecommender.DataTypeOptions,
            catalog.Frameworks.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray());
        return new OnboardingStateDto(
            row.CompanySize,
            row.Industry,
            Split(row.CloudProviders),
            Split(row.DataTypes),
            Split(row.Frameworks),
            Split(row.ScopedControls),
            OnboardingSteps.All,
            completed,
            row.AuditTargetDate,
            row.Dismissed,
            completed.Count == OnboardingSteps.All.Count,
            percent,
            options);
    }
}
