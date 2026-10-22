using System.Text;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Governance;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Tenancy;

internal static class DemoPortal
{
    public static async Task SeedAsync(
        TenantDatabases databases,
        Tenant tenant,
        DateTimeOffset now,
        EvidenceWriter evidence,
        CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName!);
        var audit = await db.Audits.FirstOrDefaultAsync(cancellationToken);
        if (audit is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(audit.Scope))
        {
            audit.Scope = Scope();
            audit.PeriodStart = new DateOnly(2026, 4, 1);
            audit.PeriodEnd = new DateOnly(2026, 9, 30);
        }

        if (!await db.Policies.AnyAsync(cancellationToken))
        {
            AddPolicies(db, now);
        }

        if (!await db.SampleRequests.AnyAsync(cancellationToken))
        {
            AddRequests(db, audit.Id, now);
        }

        if (!await db.Findings.AnyAsync(cancellationToken))
        {
            AddFindings(db, audit.Id, now);
        }

        await GovernanceExample.SeedAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await AddEvidenceAsync(db, tenant, evidence, now, cancellationToken);
    }

    private static string Scope()
    {
        var company = ClientSeed.Company;
        return $"{company.Name} ({company.LegalName}), a {company.Product}. SOC 2 Type II (Security, Availability) example engagement. The program also covers ISO 27001, ISO 27017, and ISO 27018.";
    }

    private static void AddPolicies(TenantDbContext db, DateTimeOffset now)
    {
        var lena = ClientSeed.People.First(person => person.Name == "Lena Cho");
        var priya = ClientSeed.People.First(person => person.Name == "Priya Nair");
        foreach (var template in PolicyTemplates.Core)
        {
            var document = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                Code = template.Code,
                Title = template.Title,
                OwnerName = priya.Name,
            };
            var approved = new PolicyVersion
            {
                Id = Guid.NewGuid(),
                Number = 1,
                Body = template.Body,
                State = PolicyState.Approved,
                CreatedAt = now.AddDays(-20),
                ApprovedAt = now.AddDays(-18),
                ApprovedByName = "Chandra Vell",
            };
            document.Versions.Add(approved);
            foreach (var code in template.ControlCodes)
            {
                document.Controls.Add(new PolicyControlLink { ControlCode = code });
            }

            if (template.Code == "POL-ACC")
            {
                document.Versions.Add(new PolicyVersion
                {
                    Id = Guid.NewGuid(),
                    Number = 2,
                    Body = template.Body + " Contractors use the same second factor before their account is marked active.",
                    State = PolicyState.InReview,
                    CreatedAt = now.AddDays(-2),
                });
                approved.Acknowledgements.Add(Row(lena.Name, lena.Email, now.AddDays(-3), null, now.AddDays(-1)));
                approved.Acknowledgements.Add(Row(priya.Name, priya.Email, now.AddDays(10), null, null));
            }

            if (template.Code == "POL-ISP")
            {
                foreach (var person in ClientSeed.People)
                {
                    if (person.LeaveOffset is int leave && leave <= -30)
                    {
                        continue;
                    }

                    var dueOffset = person.Training == "Overdue" ? person.TrainingDueOffset : 20;
                    var acknowledged = person.Training == "Complete" ? now.AddDays(-2) : (DateTimeOffset?)null;
                    approved.Acknowledgements.Add(Row(person.Name, person.Email, now.AddDays(dueOffset), acknowledged, null));
                }
            }

            db.Policies.Add(document);
        }
    }

    private static PolicyAcknowledgement Row(string name, string email, DateTimeOffset due, DateTimeOffset? acknowledged, DateTimeOffset? reminded) => new()
    {
        Id = Guid.NewGuid(),
        PersonName = name,
        PersonEmail = email,
        DueOn = DateOnly.FromDateTime(due.UtcDateTime),
        AcknowledgedAt = acknowledged,
        RemindedAt = reminded,
    };

    private static void AddRequests(TenantDbContext db, Guid engagementId, DateTimeOffset now)
    {
        foreach (var item in ClientSeed.Requests)
        {
            SampleResponse? response = item.Response is null
                ? null
                : Response(item.Response, "Priya Nair", ClientSeed.Moment(now, item.CreatedOffset + 1));
            db.SampleRequests.Add(Request(
                engagementId,
                item.Code,
                item.Title,
                item.Population,
                item.SampleSize,
                item.Source,
                DateOnly.FromDateTime(now.UtcDateTime).AddDays(item.DueOffset),
                Enum.Parse<SampleStatus>(item.Status),
                ClientSeed.Moment(now, item.CreatedOffset),
                response));
        }
    }

    private static SampleRequest Request(
        Guid engagementId,
        string code,
        string title,
        string population,
        int? sampleSize,
        string source,
        DateOnly due,
        SampleStatus status,
        DateTimeOffset created,
        SampleResponse? response) => new()
    {
        Id = Guid.NewGuid(),
        EngagementId = engagementId,
        Code = code,
        Title = title,
        Population = population,
        SampleSize = sampleSize,
        Source = source,
        DueOn = due,
        Status = status,
        CreatedAt = created,
        Response = response,
    };

    private static SampleResponse Response(string body, string name, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        Body = body,
        RespondedByName = name,
        RespondedAt = at,
    };

    private static void AddFindings(TenantDbContext db, Guid engagementId, DateTimeOffset now)
    {
        foreach (var item in ClientSeed.Findings)
        {
            var finding = new Finding
            {
                Id = Guid.NewGuid(),
                EngagementId = engagementId,
                Code = item.Code,
                Title = item.Title,
                Severity = Enum.Parse<FindingSeverity>(item.Severity),
                Detail = item.Detail,
                RequirementCode = item.RequirementCode,
                ManagementResponse = item.ManagementResponse,
                ResponseByName = item.ResponseByName,
            };
            foreach (var comment in item.Comments)
            {
                var row = new FindingComment
                {
                    Id = Guid.NewGuid(),
                    FindingId = finding.Id,
                    AuthorName = comment.Author,
                    Body = comment.Body,
                    At = ClientSeed.Moment(now, comment.DayOffset),
                };
                finding.Comments.Add(row);
                db.FindingComments.Add(row);
            }

            db.Findings.Add(finding);
        }
    }

    private static async Task AddEvidenceAsync(TenantDbContext db, Tenant tenant, EvidenceWriter evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.ToDictionaryAsync(item => item.Code, cancellationToken);
        foreach (var item in ClientSeed.Evidence)
        {
            if (!controls.TryGetValue(item.Control, out var controlItem))
            {
                continue;
            }

            if (await db.Evidence.AnyAsync(row => row.Title == item.Title, cancellationToken))
            {
                continue;
            }

            var evidenceId = Guid.NewGuid();
            var stored = new EvidenceItem
            {
                Id = evidenceId,
                ControlId = controlItem.Id,
                Title = item.Title,
                Source = "Manual",
                ObjectKey = tenant.Id.ToString("N") + "/" + evidenceId.ToString("N") + ".txt",
                Integrity = EvidenceIntegrity.NotStored,
            };
            db.Evidence.Add(stored);
            await evidence.AppendAsync(
                db,
                tenant.Id,
                stored,
                Encoding.UTF8.GetBytes(item.Body),
                "text/plain",
                now.AddYears(tenant.RetentionYears),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
