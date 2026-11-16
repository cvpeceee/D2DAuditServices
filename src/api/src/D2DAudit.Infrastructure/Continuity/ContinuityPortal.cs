using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Continuity;

public sealed class ContinuityPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IContinuity
{
    private static readonly string[] Criticalities = ["Critical", "High", "Medium", "Low"];

    private static readonly string[] Kinds = ["Continuity", "DisasterRecovery"];

    private static readonly string[] Results = ["Pass", "Fail"];

    public async Task<ContinuityBoard> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        return await BoardAsync(db, cancellationToken);
    }

    public async Task<Result<ImpactItem>> SaveServiceAsync(SaveImpactRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ImpactItem>.Fail("forbidden");
        }

        var name = (request.Name ?? "").Trim();
        var owner = (request.OwnerName ?? "").Trim();
        var notes = (request.Notes ?? "").Trim();
        var control = (request.ControlCode ?? "").Trim();
        if (name.Length is 0 or > 200 || !Criticalities.Contains(request.Criticality) || request.RtoHours is < 0 or > 8760 || request.RpoHours is < 0 or > 8760 || owner.Length > 200 || notes.Length > 2000 || control.Length > 32)
        {
            return Result<ImpactItem>.Fail("invalid_continuity");
        }

        await using var db = Open();
        var row = new ImpactAnalysis
        {
            Id = Guid.NewGuid(),
            Name = name,
            Criticality = request.Criticality,
            RtoHours = request.RtoHours,
            RpoHours = request.RpoHours,
            OwnerName = owner,
            ControlCode = control,
            Notes = notes,
        };
        db.ImpactAnalyses.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "continuity.service", "ok", name, null, cancellationToken);
        return Result<ImpactItem>.Ok(ToImpact(row));
    }

    public async Task<Result<PlanItem>> SavePlanAsync(SavePlanRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<PlanItem>.Fail("forbidden");
        }

        var name = (request.Name ?? "").Trim();
        var body = (request.Body ?? "").Trim();
        var control = (request.ControlCode ?? "").Trim();
        if (name.Length is 0 or > 200 || !Kinds.Contains(request.Kind) || body.Length is 0 or > 8000 || control.Length > 32)
        {
            return Result<PlanItem>.Fail("invalid_continuity");
        }

        await using var db = Open();
        var row = new ContinuityPlan
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = request.Kind,
            Body = body,
            ControlCode = control,
            UpdatedAt = time.GetUtcNow(),
        };
        db.ContinuityPlans.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "continuity.plan", "ok", name, null, cancellationToken);
        return Result<PlanItem>.Ok(ToPlan(row));
    }

    public async Task<Result<ExerciseItem>> SaveExerciseAsync(SaveExerciseRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ExerciseItem>.Fail("forbidden");
        }

        var name = (request.Name ?? "").Trim();
        var notes = (request.Notes ?? "").Trim();
        var control = (request.ControlCode ?? "").Trim();
        if (name.Length is 0 or > 200 || !Results.Contains(request.Result) || notes.Length > 2000 || control.Length > 32)
        {
            return Result<ExerciseItem>.Fail("invalid_continuity");
        }

        await using var db = Open();
        if (request.PlanId is Guid planId && !await db.ContinuityPlans.AnyAsync(item => item.Id == planId, cancellationToken))
        {
            return Result<ExerciseItem>.Fail("not_found");
        }

        var row = new RecoveryExercise
        {
            Id = Guid.NewGuid(),
            Name = name,
            PlanId = request.PlanId,
            Result = request.Result,
            Notes = notes,
            ControlCode = control,
            RanAt = time.GetUtcNow(),
        };
        db.RecoveryExercises.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "continuity.exercise", "ok", name, null, cancellationToken);
        return Result<ExerciseItem>.Ok(ToExercise(row));
    }

    internal static async Task RefreshTestAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.RecoveryExercises.AsNoTracking().ToListAsync(cancellationToken);
        var result = ContinuityRules.Evaluate(rows.Select(item => (item.Name, item.Result)));
        var controlItem = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == ContinuityRules.ControlCode, cancellationToken);
        if (controlItem is null)
        {
            return;
        }

        var test = controlItem.Tests.FirstOrDefault(item => item.Name == ContinuityRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                Name = ContinuityRules.TestName,
                Kind = TestKind.Document,
            };
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = result.Detail;
        if (result.Outcome == TestOutcome.Fail)
        {
            controlItem.Status = ControlStatus.Failing;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ContinuityBoard> BoardAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var services = await db.ImpactAnalyses.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var plans = await db.ContinuityPlans.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var exercises = await db.RecoveryExercises.AsNoTracking().OrderByDescending(item => item.RanAt).ToListAsync(cancellationToken);
        return new ContinuityBoard(services.Select(ToImpact).ToArray(), plans.Select(ToPlan).ToArray(), exercises.Select(ToExercise).ToArray());
    }

    private static ImpactItem ToImpact(ImpactAnalysis row) => new(row.Id, row.Name, row.Criticality, row.RtoHours, row.RpoHours, row.OwnerName, row.ControlCode, row.Notes);

    private static PlanItem ToPlan(ContinuityPlan row) => new(row.Id, row.Name, row.Kind, row.Body, row.ControlCode, row.UpdatedAt);

    private static ExerciseItem ToExercise(RecoveryExercise row) => new(row.Id, row.Name, row.PlanId, row.Result, row.Notes, row.ControlCode, row.RanAt);

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
