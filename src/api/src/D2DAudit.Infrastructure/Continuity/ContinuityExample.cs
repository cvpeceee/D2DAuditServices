using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Continuity;

internal static class ContinuityExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.ImpactAnalyses.Local.Any() && !await db.ImpactAnalyses.AnyAsync(cancellationToken))
        {
            db.ImpactAnalyses.Add(Service("Customer warehouse", "Critical", 4, 1, "Marcus Lee", "BC-01", "Query and load must return inside four hours."));
            db.ImpactAnalyses.Add(Service("Trust center", "Low", 24, 24, "Chandra Vell", "BC-01", "The public page can wait a day."));
            var continuity = Plan("Warehouse continuity plan", "Continuity", "Keep the warehouse read-only, restore the latest snapshot, and tell customers when query is back.", now.AddDays(-40));
            var recovery = Plan("Region failover plan", "DisasterRecovery", "Fail the warehouse to the second region and confirm the restore point is inside one hour.", now.AddDays(-20));
            db.ContinuityPlans.Add(continuity);
            db.ContinuityPlans.Add(recovery);
            db.RecoveryExercises.Add(Exercise("Quarterly warehouse restore", continuity.Id, "Pass", "The snapshot restored and a sample query matched.", now.AddDays(-20)));
            db.RecoveryExercises.Add(Exercise("Region failover drill", recovery.Id, "Fail", "The second region came up after the four-hour RTO.", now.AddDays(-12)));
        }

        var rows = db.RecoveryExercises.Local.Count > 0
            ? db.RecoveryExercises.Local.ToArray()
            : await db.RecoveryExercises.AsNoTracking().ToArrayAsync(cancellationToken);
        var result = ContinuityRules.Evaluate(rows.Select(item => (item.Name, item.Result)));
        var control = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == ContinuityRules.ControlCode, cancellationToken);
        if (control is null)
        {
            return;
        }

        var test = control.Tests.FirstOrDefault(item => item.Name == ContinuityRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = control.Id,
                Name = ContinuityRules.TestName,
                Kind = TestKind.Document,
            };
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = result.Detail;
        if (result.Outcome == TestOutcome.Fail)
        {
            control.Status = ControlStatus.Failing;
        }
    }

    private static ImpactAnalysis Service(string name, string criticality, int rto, int rpo, string owner, string control, string notes) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Criticality = criticality,
        RtoHours = rto,
        RpoHours = rpo,
        OwnerName = owner,
        ControlCode = control,
        Notes = notes,
    };

    private static ContinuityPlan Plan(string name, string kind, string body, DateTimeOffset updated) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = kind,
        Body = body,
        ControlCode = "BC-01",
        UpdatedAt = updated,
    };

    private static RecoveryExercise Exercise(string name, Guid planId, string result, string notes, DateTimeOffset ran) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PlanId = planId,
        Result = result,
        Notes = notes,
        ControlCode = "BC-01",
        RanAt = ran,
    };
}
