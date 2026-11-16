using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Incidents;

internal static class IncidentExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.Incidents.Local.Any() && !await db.Incidents.AnyAsync(cancellationToken))
        {
            var domain = ClientSeed.Company.Domain;
            Add(db, "INC-01", "Phishing message reported by success", "Low", "Resolved", now.AddDays(-12), now.AddDays(-12).AddDays(14), now.AddDays(-11), "The message was quarantined. No mailbox was signed in.", "Jordan Pell", "jordan.pell@" + domain, "IR-02");
            Add(db, "INC-02", "Warehouse token used from a new network", "High", "Investigating", now.AddDays(-1), now.AddDays(-1).AddDays(3), null, "", "Priya Nair", "priya.nair@" + domain, "AC-07");
            Add(db, "INC-03", "Backup job failed over a weekend", "Medium", "Resolved", now.AddDays(-20), now.AddDays(-20).AddDays(7), now.AddDays(-8), "The restore target filled its disk. The job was rerun after the volume was extended.", "Marcus Lee", "marcus.lee@" + domain, "BC-01");
        }

        await RefreshAsync(db, now, cancellationToken);
    }

    private static void Add(
        TenantDbContext db,
        string code,
        string title,
        string severity,
        string status,
        DateTimeOffset reported,
        DateTimeOffset due,
        DateTimeOffset? resolved,
        string cause,
        string name,
        string email,
        string control)
    {
        var row = new SecurityIncident
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title,
            Severity = severity,
            Status = status,
            Description = title,
            ReporterName = name,
            ReporterEmail = email,
            ReportedAt = reported,
            SlaDue = DateOnly.FromDateTime(due.UtcDateTime),
            ResolvedAt = resolved,
            RootCause = cause,
            ControlCode = control,
        };
        row.Timeline.Add(new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = row.Id,
            Kind = "Reported",
            Detail = title,
            AuthorName = name,
            At = reported,
        });
        if (status == "Resolved" && resolved is DateTimeOffset closed)
        {
            row.Timeline.Add(new IncidentEvent
            {
                Id = Guid.NewGuid(),
                IncidentId = row.Id,
                Kind = "Resolved",
                Detail = cause,
                AuthorName = name,
                At = closed,
            });
        }

        db.Incidents.Add(row);
    }

    private static async Task RefreshAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var rows = db.Incidents.Local.Count > 0 ? db.Incidents.Local.ToArray() : await db.Incidents.AsNoTracking().ToArrayAsync(cancellationToken);
        var result = IncidentRules.Evaluate(rows.Select(item => (item.Code, item.Status, item.SlaDue, item.ResolvedAt)), today);
        var control = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "IR-02", cancellationToken);
        if (control is null)
        {
            return;
        }

        var test = control.Tests.FirstOrDefault(item => item.Name == IncidentRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = control.Id,
                Name = IncidentRules.TestName,
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
