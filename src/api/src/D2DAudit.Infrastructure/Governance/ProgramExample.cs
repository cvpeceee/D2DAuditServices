using System.Text.Json;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Governance;

internal static class ProgramExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var snapshots = await db.Resources.AsNoTracking().ToListAsync(cancellationToken);
        EnrichPeople(db, snapshots, today);
        if (!db.Assets.Local.Any() && !await db.Assets.AnyAsync(cancellationToken))
        {
            AddAssets(db, snapshots, today);
        }

        if (!db.TrainingModules.Local.Any() && !await db.TrainingModules.AnyAsync(cancellationToken))
        {
            AddTraining(db, today, now);
        }

        if (!db.WorkTasks.Local.Any() && !await db.WorkTasks.AnyAsync(cancellationToken))
        {
            AddTasks(db, today);
        }

        if (!db.AccessCampaigns.Local.Any(item => item.State == CampaignState.Draft))
        {
            db.AccessCampaigns.Add(new AccessCampaign
            {
                Id = Guid.NewGuid(),
                Name = "Q1 contractor review",
                DueOn = today.AddDays(40),
                State = CampaignState.Draft,
                CreatedAt = now,
                Providers = "github",
            });
        }
    }

    private static void EnrichPeople(TenantDbContext db, IReadOnlyList<ResourceSnapshot> snapshots, DateOnly today)
    {
        var devices = db.Devices.Local.Select(item => item.OwnerEmail).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var person in db.People.Local)
        {
            person.MfaEnrolled = Mfa(snapshots, person.Email);
            person.DeviceStatus = devices.Contains(person.Email) ? "Reporting" : "Missing";
            var tenured = person.HireOn is DateOnly hired && hired.AddDays(30) <= today;
            if (person.State == PersonState.Active && !tenured)
            {
                person.BackgroundStatus = "Pending";
            }
            else if (tenured)
            {
                person.BackgroundStatus = "Clear";
                person.BackgroundOn = person.HireOn?.AddDays(14);
            }

            if (person.Training == TrainingState.Complete)
            {
                person.TrainingCompletedOn = today.AddDays(-40);
            }

            if (person.Email.StartsWith("sam.leaved@", StringComparison.OrdinalIgnoreCase))
            {
                person.StatusReason = "Leave date confirmed by People.";
            }
        }
    }

    private static bool Mfa(IReadOnlyList<ResourceSnapshot> snapshots, string email)
    {
        var active = snapshots.Where(item => item.Kind == "user" && SameEmail(item, email) && Active(item)).ToArray();
        return active.Length > 0 && active.All(item => Attribute(item, "mfa") != "false");
    }

    private static bool SameEmail(ResourceSnapshot snapshot, string email)
    {
        var value = Attribute(snapshot, "email");
        return value.Equals(email, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Active(ResourceSnapshot snapshot) => Attribute(snapshot, "status") == "active";

    private static string Attribute(ResourceSnapshot snapshot, string name)
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.AttributesJson);
        return attributes is not null && attributes.TryGetValue(name, out var value) ? value : "";
    }

    private static void AddAssets(TenantDbContext db, IReadOnlyList<ResourceSnapshot> snapshots, DateOnly today)
    {
        var domain = ClientSeed.Company.Domain;
        var seen = new DateTimeOffset(today.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        foreach (var snapshot in snapshots)
        {
            var type = TypeOf(snapshot.Kind);
            if (type.Length == 0)
            {
                continue;
            }

            var owner = snapshot.Kind == "repository"
                ? ("Dev Patel", "dev.patel@" + domain)
                : ("Priya Nair", "priya.nair@" + domain);
            Add(db, snapshot.Name, type, snapshot.Provider, owner.Item1, owner.Item2, EnvironmentOf(snapshot.Name), ClassificationOf(snapshot), InScope(snapshot.Name), seen, snapshot.Provider + ":" + snapshot.Kind + ":" + snapshot.ExternalId);
        }

        foreach (var cloud in new[] { "aws", "azure", "gcp" })
        {
            Add(db, cloud + " warehouse", "Database", cloud, "Priya Nair", "priya.nair@" + domain, "production", "Restricted", true, seen, cloud + ":database:warehouse");
        }

        Add(db, "Google Workspace", "Saas", "google", "Lena Cho", "lena.cho@" + domain, "production", "Confidential", true, seen, "saas:google");
        Add(db, "Microsoft 365", "Saas", "microsoft", "Lena Cho", "lena.cho@" + domain, "production", "Confidential", true, seen, "saas:microsoft");
        Add(db, "GitHub", "Saas", "github", "Dev Patel", "dev.patel@" + domain, "production", "Confidential", true, seen, "saas:github");
        Add(db, ClientSeed.Company.Name, "Saas", "manual", "Chandra Vell", "chandra.vell@" + domain, "production", "Restricted", true, seen, "saas:product");

        foreach (var device in db.Devices.Local)
        {
            var when = new DateTimeOffset(device.LastSeenOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            Add(db, device.OwnerName + " " + device.Platform, "Laptop", "agent", device.OwnerName, device.OwnerEmail, "production", "Internal", true, when, "laptop:" + device.Serial);
        }
    }

    private static void Add(TenantDbContext db, string name, string type, string source, string owner, string email, string environment, string classification, bool inScope, DateTimeOffset seen, string externalId)
    {
        db.Assets.Add(new TrackedAsset
        {
            Id = Guid.NewGuid(),
            Name = name,
            AssetType = type,
            Source = source,
            OwnerName = owner,
            OwnerEmail = email,
            Environment = environment,
            Classification = classification,
            InScope = inScope,
            LastSeen = seen,
            ExternalId = externalId,
        });
    }

    private static string TypeOf(string kind) => kind switch
    {
        "bucket" => "Bucket",
        "repository" => "Repository",
        "root" or "subscription" or "project" => "Server",
        "database" => "Database",
        _ => "",
    };

    private static string EnvironmentOf(string name)
    {
        if (name.Contains("staging", StringComparison.OrdinalIgnoreCase) || name.Contains("experiment", StringComparison.OrdinalIgnoreCase) || name.Equals("analytics", StringComparison.OrdinalIgnoreCase))
        {
            return "staging";
        }

        return "production";
    }

    private static string ClassificationOf(ResourceSnapshot snapshot)
    {
        if (Attribute(snapshot, "public") == "true")
        {
            return "Public";
        }

        if (snapshot.Name.Contains("customer", StringComparison.OrdinalIgnoreCase) || snapshot.Name.Contains("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            return "Restricted";
        }

        return snapshot.Kind == "repository" ? "Internal" : "Confidential";
    }

    private static bool InScope(string name) =>
        !name.Equals("build-artifacts", StringComparison.OrdinalIgnoreCase) && !name.Equals("analytics", StringComparison.OrdinalIgnoreCase);

    private static void AddTraining(TenantDbContext db, DateOnly today, DateTimeOffset now)
    {
        var annual = new TrainingModule
        {
            Id = Guid.NewGuid(),
            Code = TrainingContent.AnnualCode,
            Title = "Annual security awareness",
            ModuleType = "BuiltIn",
            AssignedGroup = "Everyone",
            DueOn = today.AddDays(30),
        };
        var lms = new TrainingModule
        {
            Id = Guid.NewGuid(),
            Code = TrainingContent.LmsCode,
            Title = "Role-specific course",
            ModuleType = "External",
            AssignedGroup = "Everyone",
            DueOn = today.AddDays(30),
            ExternalUrl = string.Concat("https", "://", "learn.", ClientSeed.Company.Domain, "/annual"),
        };
        db.TrainingModules.Add(annual);
        db.TrainingModules.Add(lms);
        var complete = db.People.Local.Where(item => item.Training == TrainingState.Complete).Select(item => item.Email).ToArray();
        for (var index = 0; index < complete.Length; index++)
        {
            db.TrainingCompletions.Add(Completion(annual.Id, complete[index], now.AddDays(-40)));
            if (index % 7 != 0)
            {
                db.TrainingCompletions.Add(Completion(lms.Id, complete[index], now.AddDays(-20)));
            }
        }
    }

    private static TrainingCompletion Completion(Guid moduleId, string email, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        ModuleId = moduleId,
        PersonEmail = email,
        CompletedAt = at,
        Score = TrainingContent.Questions.Count,
    };

    private static void AddTasks(TenantDbContext db, DateOnly today)
    {
        var domain = ClientSeed.Company.Domain;
        AddTask(db, "Close the MFA gap", "Test", "mfa", "Priya Nair", "priya.nair@" + domain, today.AddDays(-3), "Open", "None");
        AddTask(db, "Review the leaver risk", "Risk", "R-03", "Priya Nair", "priya.nair@" + domain, today.AddDays(-2), "Open", "None");
        AddTask(db, "Refresh the Pixel Fonts questionnaire", "Vendor", "Pixel Fonts", "Lena Cho", "lena.cho@" + domain, today.AddDays(-12), "Open", "Annual");
        AddTask(db, "Refresh the information security policy", "Policy", "POL-ISP", "Priya Nair", "priya.nair@" + domain, today.AddDays(14), "Open", "Annual");
        AddTask(db, "Confirm the marketing site control", "Control", "CFG-01", "Marcus Lee", "marcus.lee@" + domain, today.AddDays(5), "InProgress", "None");
        AddTask(db, "Prepare the next access review", "Control", "AC-07", "Chandra Vell", "chandra.vell@" + domain, today.AddDays(20), "Open", "Quarterly");
        AddTask(db, "File the last vendor review", "Vendor", "Mailfold", "Lena Cho", "lena.cho@" + domain, today.AddDays(-40), "Done", "Annual");
        AddTask(db, "Protect the experiments branch", "Control", "CM-11", "Dev Patel", "dev.patel@" + domain, today.AddDays(-2), "Open", "None");
    }

    private static void AddTask(TenantDbContext db, string title, string kind, string code, string owner, string email, DateOnly due, string status, string recurrence)
    {
        db.WorkTasks.Add(new WorkTask
        {
            Id = Guid.NewGuid(),
            Title = title,
            LinkedKind = kind,
            LinkedCode = code,
            OwnerName = owner,
            OwnerEmail = email,
            DueOn = due,
            Status = status,
            Recurrence = recurrence,
        });
    }
}
