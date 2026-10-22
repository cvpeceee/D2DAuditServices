using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Governance;

internal static class GovernanceExample
{
    public static async Task SeedAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Risks.AnyAsync(cancellationToken))
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        AddRisk(db, "R-01", "Public storage", "The marketing site bucket can be read by anyone on the internet.", 4, 5, "Priya N.", "priya@acme.example", RiskTreatment.Mitigate, "Turn on Block Public Access and confirm the next collection.", today.AddDays(7), 2, 3, ["CFG-01"]);
        AddRisk(db, "R-02", "Missing MFA", "Two workforce accounts can sign in with a password alone.", 4, 4, "Priya N.", "priya@acme.example", RiskTreatment.Mitigate, "Require a second factor before the account is marked active.", today.AddDays(14), 2, 2, ["AC-07"]);
        AddRisk(db, "R-03", "Leaver still has access", "An offboarded person still has a GitHub account.", 3, 5, "Lena K.", "lena@acme.example", RiskTreatment.Mitigate, "Remove the GitHub account and record it in the access review.", today.AddDays(-2), 1, 2, ["HR-08"]);
        AddRisk(db, "R-04", "Unprotected branch", "The experiments repository can be changed without a second reviewer.", 3, 3, "Dev T.", "dev@acme.example", RiskTreatment.Mitigate, "Protect the default branch.", today.AddDays(21), 1, 2, ["CM-11"]);
        AddRisk(db, "R-05", "Vendor concentration", "Source hosting and identity both sit with one or two suppliers.", 2, 4, "Chandra V.", "chandra@acme.example", RiskTreatment.Transfer, "Keep the suppliers' SOC 2 reports and a named exit path.", today.AddDays(40), 2, 3, []);

        await SyncPeopleAsync(db, today, cancellationToken);
        AddVendor(db, "GitHub", "Source hosting", VendorCriticality.Critical, "Source code and pull requests", VendorReviewState.Approved, today.AddDays(60), 3, "GitHub SOC 2 report", "soc2", today.AddDays(-40));
        AddVendor(db, "Google Workspace", "Email and directory", VendorCriticality.High, "Mail, files, and the employee directory", VendorReviewState.InReview, today.AddDays(10), 3, "Workspace security whitepaper", "whitepaper", today.AddDays(-80));
        AddVendor(db, "Amazon Web Services", "Production hosting", VendorCriticality.Critical, "Customer application data in example buckets", VendorReviewState.Approved, today.AddDays(90), 4, "AWS SOC 2 report", "soc2", today.AddDays(-20));
        AddVendor(db, "Pixel Fonts", "Marketing site fonts", VendorCriticality.Low, "No customer data", VendorReviewState.Expired, today.AddDays(-12), 1, "Font license", "license", today.AddDays(-400));

        if (!await db.Resources.AnyAsync(item => item.Kind == "user", cancellationToken) || await db.AccessCampaigns.AnyAsync(cancellationToken))
        {
            return;
        }

        var campaign = new AccessCampaign
        {
            Id = Guid.NewGuid(),
            Name = "Q4 access review",
            DueOn = today.AddDays(12),
            State = CampaignState.Open,
            CreatedAt = now.AddDays(-1),
        };
        var accounts = await db.PersonAccounts.Include(item => item.Person).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var item = new AccessReviewItem
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                PersonName = account.Person?.Name ?? account.AccountName,
                PersonEmail = account.Person?.Email ?? "",
                Provider = account.Provider,
                AccountName = account.AccountName,
                Decision = AccessDecision.Pending,
            };
            campaign.Items.Add(item);
            db.AccessItems.Add(item);
        }

        db.AccessCampaigns.Add(campaign);
    }

    private static async Task SyncPeopleAsync(TenantDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var snapshots = await db.Resources.AsNoTracking().Where(item => item.Kind == "user").ToListAsync(cancellationToken);
        foreach (var person in DirectorySync.People(snapshots.Select(Read).OfType<DirectoryAccount>()))
        {
            var row = new DirectoryPerson
            {
                Id = Guid.NewGuid(),
                Name = person.Name,
                Email = person.Email,
                Department = Department(person.Email),
                Source = DirectorySync.SourceOf(person.Accounts),
                State = person.State,
                Training = Training(person.Email),
                TrainingDueOn = TrainingDue(person.Email, today),
            };
            foreach (var code in ChecklistTasks.Onboarding())
            {
                var task = new PersonTask
                {
                    Id = Guid.NewGuid(),
                    PersonId = row.Id,
                    Kind = ChecklistKind.Onboarding,
                    TaskCode = code,
                    Done = person.State != PersonState.Offboarded && code != "equipment",
                };
                row.Tasks.Add(task);
                db.PersonTasks.Add(task);
            }

            foreach (var code in ChecklistTasks.Offboarding())
            {
                var task = new PersonTask
                {
                    Id = Guid.NewGuid(),
                    PersonId = row.Id,
                    Kind = ChecklistKind.Offboarding,
                    TaskCode = code,
                    Done = false,
                };
                row.Tasks.Add(task);
                db.PersonTasks.Add(task);
            }

            foreach (var account in person.Accounts)
            {
                var access = new PersonAccount
                {
                    Id = Guid.NewGuid(),
                    PersonId = row.Id,
                    Provider = account.Provider,
                    ExternalId = account.ExternalId,
                    AccountName = account.ExternalId,
                    Active = account.Active,
                };
                row.Accounts.Add(access);
                db.PersonAccounts.Add(access);
            }

            db.People.Add(row);
        }
    }

    private static DirectoryAccount? Read(ResourceSnapshot snapshot)
    {
        var attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.AttributesJson);
        if (attributes is null || !attributes.TryGetValue("email", out var email))
        {
            return null;
        }

        attributes.TryGetValue("offboarded", out var offboarded);
        attributes.TryGetValue("status", out var status);
        return new DirectoryAccount(snapshot.Provider, snapshot.ExternalId, snapshot.Name, email, offboarded == "true", status == "active");
    }

    private static void AddRisk(
        TenantDbContext db,
        string code,
        string title,
        string statement,
        int likelihood,
        int impact,
        string owner,
        string email,
        RiskTreatment treatment,
        string plan,
        DateOnly reviewOn,
        int residualLikelihood,
        int residualImpact,
        IReadOnlyList<string> controls)
    {
        var risk = new RiskItem
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title,
            Statement = statement,
            Likelihood = likelihood,
            Impact = impact,
            OwnerName = owner,
            OwnerEmail = email,
            Treatment = treatment,
            TreatmentPlan = plan,
            ReviewOn = reviewOn,
            ResidualLikelihood = residualLikelihood,
            ResidualImpact = residualImpact,
        };
        foreach (var controlCode in controls)
        {
            var link = new RiskControlLink { RiskId = risk.Id, ControlCode = controlCode };
            risk.Controls.Add(link);
            db.RiskControls.Add(link);
        }

        db.Risks.Add(risk);
    }

    private static void AddVendor(
        TenantDbContext db,
        string name,
        string service,
        VendorCriticality criticality,
        string dataAccess,
        VendorReviewState review,
        DateOnly due,
        int rating,
        string documentTitle,
        string kind,
        DateOnly issued)
    {
        var vendor = new VendorRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Service = service,
            Criticality = criticality,
            DataAccess = dataAccess,
            ReviewState = review,
            ReviewDueOn = due,
            RiskRating = rating,
        };
        var document = new VendorDocument
        {
            Id = Guid.NewGuid(),
            VendorId = vendor.Id,
            Title = documentTitle,
            Kind = kind,
            IssuedOn = issued,
        };
        vendor.Documents.Add(document);
        db.VendorDocuments.Add(document);
        db.Vendors.Add(vendor);
    }

    private static string Department(string email) => email switch
    {
        "priya@acme.example" => "Security",
        "lena@acme.example" => "People",
        "dev@acme.example" => "Engineering",
        _ => "Workforce",
    };

    private static TrainingState Training(string email) => email switch
    {
        "priya@acme.example" => TrainingState.Complete,
        "lena@acme.example" or "sam.leaved@acme.example" => TrainingState.Overdue,
        _ => TrainingState.Assigned,
    };

    private static DateOnly TrainingDue(string email, DateOnly today) => email switch
    {
        "priya@acme.example" => today.AddDays(-30),
        "lena@acme.example" or "sam.leaved@acme.example" => today.AddDays(-6),
        _ => today.AddDays(20),
    };
}
