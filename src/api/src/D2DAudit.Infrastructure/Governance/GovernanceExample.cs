using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
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
        foreach (var item in ClientSeed.Risks)
        {
            AddRisk(db, item, today);
        }

        await SyncPeopleAsync(db, today, cancellationToken);
        foreach (var item in ClientSeed.Vendors)
        {
            AddVendor(db, item, today);
        }

        foreach (var week in ClientSeed.History)
        {
            db.CheckWeeks.Add(new CheckWeek
            {
                Id = Guid.NewGuid(),
                WeekOf = today.AddDays(week.Week * 7),
                Passing = week.Passing,
                Failing = week.Failing,
            });
        }

        foreach (var note in ClientSeed.Notifications)
        {
            db.Notifications.Add(new WorkspaceNotification
            {
                Id = Guid.NewGuid(),
                Title = note.Title,
                Body = note.Body,
                Href = note.Href,
                At = ClientSeed.Moment(now, note.DayOffset),
            });
        }

        foreach (var device in ClientSeed.Devices)
        {
            db.Devices.Add(new EnrolledDevice
            {
                Id = Guid.NewGuid(),
                OwnerName = device.OwnerName,
                OwnerEmail = device.OwnerEmail,
                Platform = device.Platform,
                Serial = device.Serial,
                LastSeenOn = today.AddDays(device.SeenOffset),
            });
        }

        if (!await db.Resources.AnyAsync(item => item.Kind == "user", cancellationToken) || await db.AccessCampaigns.AnyAsync(cancellationToken))
        {
            return;
        }

        var review = ClientSeed.AccessReview;
        var campaign = new AccessCampaign
        {
            Id = Guid.NewGuid(),
            Name = review.Name,
            DueOn = today.AddDays(review.DueOffset),
            State = CampaignState.Open,
            CreatedAt = ClientSeed.Moment(now, review.StartedOffset),
        };
        var accounts = await db.PersonAccounts.Include(item => item.Person).ToListAsync(cancellationToken);
        var ordered = accounts
            .OrderBy(item => item.Person?.Email ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Provider, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var account = ordered[index];
            var decision = ClientSeed.DecisionFor(index);
            var item = new AccessReviewItem
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                PersonName = account.Person?.Name ?? account.AccountName,
                PersonEmail = account.Person?.Email ?? "",
                Provider = account.Provider,
                AccountName = account.AccountName,
                Decision = decision,
                ReviewerName = decision == AccessDecision.Pending ? null : "Priya Nair",
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
            var profile = ClientSeed.Person(person.Email);
            var row = new DirectoryPerson
            {
                Id = Guid.NewGuid(),
                Name = person.Name,
                Email = person.Email,
                Department = profile?.Department ?? "Workforce",
                Role = profile?.Role ?? "",
                HireOn = profile is null ? null : today.AddDays(profile.HireOffset),
                LeftOn = profile?.LeaveOffset is int leave ? today.AddDays(leave) : null,
                Source = DirectorySync.SourceOf(person.Accounts),
                State = person.State,
                Training = profile is not null && Enum.TryParse<TrainingState>(profile.Training, out var training) ? training : TrainingState.Assigned,
                TrainingDueOn = today.AddDays(profile?.TrainingDueOffset ?? 20),
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

    private static void AddRisk(TenantDbContext db, ClientRisk item, DateOnly today)
    {
        var risk = new RiskItem
        {
            Id = Guid.NewGuid(),
            Code = item.Code,
            Title = item.Title,
            Statement = item.Statement,
            Likelihood = item.Likelihood,
            Impact = item.Impact,
            OwnerName = item.OwnerName,
            OwnerEmail = item.OwnerEmail,
            Treatment = Enum.Parse<RiskTreatment>(item.Treatment),
            TreatmentPlan = item.TreatmentPlan,
            ReviewOn = today.AddDays(item.ReviewOffset),
            ResidualLikelihood = item.ResidualLikelihood,
            ResidualImpact = item.ResidualImpact,
        };
        foreach (var controlCode in item.ControlCodes)
        {
            var link = new RiskControlLink { RiskId = risk.Id, ControlCode = controlCode };
            risk.Controls.Add(link);
            db.RiskControls.Add(link);
        }

        db.Risks.Add(risk);
    }

    private static void AddVendor(TenantDbContext db, ClientVendor item, DateOnly today)
    {
        var vendor = new VendorRecord
        {
            Id = Guid.NewGuid(),
            Name = item.Name,
            Service = item.Service,
            Criticality = Enum.Parse<VendorCriticality>(item.Criticality),
            DataAccess = item.DataAccess,
            ReviewState = Enum.Parse<VendorReviewState>(item.ReviewState),
            ReviewDueOn = today.AddDays(item.ReviewOffset),
            RiskRating = item.RiskRating,
        };
        foreach (var file in item.Documents)
        {
            var document = new VendorDocument
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Title = file.Title,
                Kind = file.Kind,
                IssuedOn = today.AddDays(file.IssuedOffset),
            };
            vendor.Documents.Add(document);
            db.VendorDocuments.Add(document);
        }

        db.Vendors.Add(vendor);
    }
}
