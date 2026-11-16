using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Incidents;

public sealed class IncidentPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : IIncidents
{
    private static readonly string[] Severities = ["Critical", "High", "Medium", "Low"];

    private static readonly string[] Statuses = ["Open", "Investigating", "Resolved"];

    public async Task<IReadOnlyList<IncidentItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Incidents.AsNoTracking().Include(item => item.Timeline).OrderByDescending(item => item.ReportedAt).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return rows.Select(row => ToItem(row, today)).ToArray();
    }

    public async Task<Result<IncidentItem>> ReportAsync(ReportIncidentRequest request, CancellationToken cancellationToken)
    {
        var title = (request.Title ?? "").Trim();
        var description = (request.Description ?? "").Trim();
        if (title.Length is 0 or > 200 || description.Length is 0 or > 4000 || !Severities.Contains(request.Severity))
        {
            return Result<IncidentItem>.Fail("invalid_incident");
        }

        await using var db = Open();
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var row = new SecurityIncident
        {
            Id = Guid.NewGuid(),
            Code = await NextCodeAsync(db, cancellationToken),
            Title = title,
            Severity = request.Severity,
            Status = "Open",
            Description = description,
            ReporterName = tenant.DisplayName,
            ReporterEmail = tenant.Email,
            ReportedAt = now,
            SlaDue = today.AddDays(IncidentRules.Days(request.Severity)),
        };
        row.Timeline.Add(new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = row.Id,
            Kind = "Reported",
            Detail = description,
            AuthorName = tenant.DisplayName,
            At = now,
        });
        db.Incidents.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "incident.reported", "ok", row.Code, null, cancellationToken);
        return Result<IncidentItem>.Ok(ToItem(row, today));
    }

    public async Task<Result<IncidentItem>> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<IncidentItem>.Fail("forbidden");
        }

        var status = request.Status ?? "";
        if (status.Length > 0 && !Statuses.Contains(status))
        {
            return Result<IncidentItem>.Fail("invalid_incident");
        }

        var note = (request.Note ?? "").Trim();
        var cause = (request.RootCause ?? "").Trim();
        if (note.Length > 2000 || cause.Length > 2000 || (request.ControlCode ?? "").Trim().Length > 32)
        {
            return Result<IncidentItem>.Fail("invalid_incident");
        }

        await using var db = Open();
        var row = await db.Incidents.Include(item => item.Timeline).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<IncidentItem>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        if (status.Length > 0 && status != row.Status)
        {
            row.Status = status;
            row.ResolvedAt = status == "Resolved" ? now : null;
            row.Timeline.Add(Event(row.Id, "Status", status, now));
        }

        if (request.RootCause is not null)
        {
            row.RootCause = cause;
        }

        if (request.ControlCode is not null)
        {
            row.ControlCode = request.ControlCode.Trim();
        }

        if (request.EvidenceId is Guid evidenceId)
        {
            var exists = await db.Evidence.AnyAsync(item => item.Id == evidenceId, cancellationToken);
            if (!exists)
            {
                return Result<IncidentItem>.Fail("not_found");
            }

            row.EvidenceId = evidenceId;
        }

        if (note.Length > 0)
        {
            row.Timeline.Add(Event(row.Id, "Note", note, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "incident.updated", "ok", row.Code, null, cancellationToken);
        return Result<IncidentItem>.Ok(ToItem(row, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    private async Task RefreshTestAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var rows = await db.Incidents.AsNoTracking().ToListAsync(cancellationToken);
        var result = IncidentRules.Evaluate(rows.Select(item => (item.Code, item.Status, item.SlaDue, item.ResolvedAt)), today);
        var controlItem = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "IR-02", cancellationToken);
        if (controlItem is null)
        {
            return;
        }

        var test = controlItem.Tests.FirstOrDefault(item => item.Name == IncidentRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                Name = IncidentRules.TestName,
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

    private IncidentEvent Event(Guid incidentId, string kind, string detail, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        Kind = kind,
        Detail = detail,
        AuthorName = tenant.DisplayName,
        At = at,
    };

    private static async Task<string> NextCodeAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var codes = await db.Incidents.Select(item => item.Code).ToListAsync(cancellationToken);
        var number = codes.Select(code => code.StartsWith("INC-", StringComparison.Ordinal) && int.TryParse(code[4..], out var value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return "INC-" + number.ToString("00");
    }

    private static IncidentItem ToItem(SecurityIncident row, DateOnly today) => new(
        row.Id,
        row.Code,
        row.Title,
        row.Severity,
        row.Status,
        row.Description,
        row.ReporterName,
        row.ReporterEmail,
        row.ReportedAt,
        row.SlaDue,
        row.ResolvedAt,
        row.RootCause,
        row.ControlCode,
        row.EvidenceId,
        IncidentRules.WithinSla(row.Status, row.SlaDue, row.ResolvedAt, today),
        row.Timeline.OrderBy(item => item.At).Select(item => new IncidentEventDto(item.Kind, item.Detail, item.AuthorName, item.At)).ToArray());

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
