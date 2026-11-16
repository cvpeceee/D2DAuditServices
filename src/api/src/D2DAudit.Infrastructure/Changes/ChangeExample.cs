using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Changes;

internal static class ChangeExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.Changes.Local.Any() && !await db.Changes.AnyAsync(cancellationToken))
        {
            Add(db, "CHG-01", "Rotate the warehouse credential helper", "github", "harborline/warehouse#482", "harborline/warehouse", "Dev Patel", "Priya Nair", now.AddDays(-6), now.AddDays(-5), now.AddDays(-5), "a1b2c3d");
            Add(db, "CHG-02", "Open a temporary rule on the staging network", "github", "harborline/warehouse#501", "harborline/warehouse", "Dev Patel", "", now.AddDays(-2), null, now.AddDays(-2), "e4f5a6b");
            Add(db, "CHG-03", "Expand the backup window", "jira", "HL-214", "HL", "Marcus Lee", "", now.AddDays(-1), null, null, "");
        }

        await RefreshAsync(db, cancellationToken);
    }

    private static void Add(
        TenantDbContext db,
        string code,
        string title,
        string source,
        string key,
        string repository,
        string author,
        string approver,
        DateTimeOffset opened,
        DateTimeOffset? approved,
        DateTimeOffset? deployed,
        string deployRef)
    {
        db.Changes.Add(new ChangeRecord
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title,
            Source = source,
            ExternalKey = key,
            Repository = repository,
            AuthorName = author,
            ApproverName = approver,
            ApprovedAt = approved,
            DeployRef = deployRef,
            DeployedAt = deployed,
            OpenedAt = opened,
            Status = deployed is not null ? "Deployed" : "Open",
        });
    }

    private static async Task RefreshAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = db.Changes.Local.Count > 0 ? db.Changes.Local.ToArray() : await db.Changes.AsNoTracking().ToArrayAsync(cancellationToken);
        var result = ChangeRules.Evaluate(rows.Select(item => (item.Code, item.ApprovedAt, item.DeployedAt)));
        var control = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == ChangeRules.ControlCode, cancellationToken);
        if (control is null)
        {
            return;
        }

        var test = control.Tests.FirstOrDefault(item => item.Name == ChangeRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = control.Id,
                Name = ChangeRules.TestName,
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
}
