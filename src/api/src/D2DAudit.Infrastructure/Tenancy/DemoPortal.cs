using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Tenancy;

internal static class DemoPortal
{
    public static async Task SeedAsync(TenantDatabases databases, Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName!);
        var audit = await db.Audits.FirstOrDefaultAsync(cancellationToken);
        if (audit is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(audit.Scope))
        {
            audit.Scope = "SOC 2 Type II (Security, Availability). Example engagement for Acme Robotics.";
            audit.PeriodStart = new DateOnly(2026, 4, 1);
            audit.PeriodEnd = new DateOnly(2026, 9, 30);
        }

        if (!await db.Policies.AnyAsync(cancellationToken))
        {
            AddPolicies(db, now);
        }

        if (!await db.SampleRequests.AnyAsync(cancellationToken))
        {
            var evidenceId = await db.Evidence.OrderBy(item => item.Title).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
            AddRequests(db, audit.Id, now, evidenceId);
        }

        if (!await db.Findings.AnyAsync(cancellationToken))
        {
            AddFindings(db, audit.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddPolicies(TenantDbContext db, DateTimeOffset now)
    {
        foreach (var template in PolicyTemplates.Core)
        {
            var document = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                Code = template.Code,
                Title = template.Title,
                OwnerName = "Priya N.",
            };
            var approved = new PolicyVersion
            {
                Id = Guid.NewGuid(),
                Number = 1,
                Body = template.Body,
                State = PolicyState.Approved,
                CreatedAt = now.AddDays(-20),
                ApprovedAt = now.AddDays(-18),
                ApprovedByName = "Chandra V.",
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
                approved.Acknowledgements.Add(Row("Lena K.", "lena@acme.example", now.AddDays(-3), null, now.AddDays(-1)));
                approved.Acknowledgements.Add(Row("Priya N.", "priya@acme.example", now.AddDays(10), null, null));
            }

            if (template.Code == "POL-ISP")
            {
                approved.Acknowledgements.Add(Row("Dev T.", "dev@acme.example", now.AddDays(-5), now.AddDays(-4), null));
                approved.Acknowledgements.Add(Row("People Ops", "people@acme.example", now.AddDays(-5), now.AddDays(-3), null));
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

    private static void AddRequests(TenantDbContext db, Guid engagementId, DateTimeOffset now, Guid? evidenceId)
    {
        db.SampleRequests.Add(Request(engagementId, "REQ-14", "New-hire onboarding", "23 hires", 5, "HR system", new DateOnly(2026, 10, 24), SampleStatus.Open, now.AddDays(-1), null));
        db.SampleRequests.Add(Request(engagementId, "REQ-13", "Change tickets", "612 PRs", 25, "GitHub", new DateOnly(2026, 10, 23), SampleStatus.Submitted, now.AddDays(-2), Response("The pull request list for the sample is attached.", "Priya N.", now.AddHours(-30), null)));
        db.SampleRequests.Add(Request(engagementId, "REQ-12", "Terminations", "9 leavers", 4, "Workspace", new DateOnly(2026, 10, 22), SampleStatus.Open, now.AddDays(-3), null));
        db.SampleRequests.Add(Request(engagementId, "REQ-11", "Q2 access review", "Full population", null, "Manual", new DateOnly(2026, 10, 18), SampleStatus.Accepted, now.AddDays(-8), Response("The completed review is in the evidence library.", "Priya N.", now.AddDays(-6), evidenceId)));
        db.SampleRequests.Add(Request(engagementId, "REQ-10", "Backup restores", "6 restores", 2, "Azure", new DateOnly(2026, 10, 16), SampleStatus.Accepted, now.AddDays(-10), Response("Both restore records are in the evidence library.", "Dev T.", now.AddDays(-7), null)));
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

    private static SampleResponse Response(string body, string name, DateTimeOffset at, Guid? evidenceId) => new()
    {
        Id = Guid.NewGuid(),
        Body = body,
        RespondedByName = name,
        RespondedAt = at,
        EvidenceId = evidenceId,
    };

    private static void AddFindings(TenantDbContext db, Guid engagementId)
    {
        db.Findings.Add(new Finding
        {
            Id = Guid.NewGuid(),
            EngagementId = engagementId,
            Code = "F-02",
            Title = "MFA gap",
            Severity = FindingSeverity.Exception,
            Detail = "3 GitHub accounts lacked MFA for 11 days in July.",
            RequirementCode = "CC6.1",
            ManagementResponse = "Those accounts are enrolled now. The access review will check MFA before the account is marked active.",
            ResponseByName = "Priya N.",
        });
        db.Findings.Add(new Finding
        {
            Id = Guid.NewGuid(),
            EngagementId = engagementId,
            Code = "F-01",
            Title = "Late access review",
            Severity = FindingSeverity.Observation,
            Detail = "The Q2 review was completed 9 days after the due date.",
            RequirementCode = "CC6.2",
            ManagementResponse = "The review calendar now reminds the owner a week before the due date. Evidence is on REQ-11.",
            ResponseByName = "Priya N.",
        });
    }
}
