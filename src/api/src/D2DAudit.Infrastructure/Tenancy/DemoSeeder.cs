using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Identity;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Infrastructure.Tenancy;

public sealed class DemoSeeder(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    ITenantProvisioner provisioner,
    PasswordHasher<UserAccount> passwords,
    TimeProvider time,
    IServiceScopeFactory scopes) : IDemoSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var acme = await RequiredTenantAsync(provisioner, new CreateTenantRequest(ClientSeed.Company.Slug, ClientSeed.Company.Name, ClientSeed.Company.Region, true), cancellationToken);
        var northwind = await RequiredTenantAsync(provisioner, new CreateTenantRequest("northwind-labs", "Northwind Labs", "EU", true), cancellationToken);
        await provisioner.SeedExampleControlsAsync(acme.Id, cancellationToken);
        await EnsureNorthwindMarkerAsync(northwind, cancellationToken);
        await provisioner.SeedExampleControlsAsync(northwind.Id, cancellationToken);
        await EnsureAuditAsync(acme.Id, cancellationToken);

        var chandra = await EnsureUserAsync("chandra.vell@harborline.example", "Chandra Vell", cancellationToken);
        var priya = await EnsureUserAsync("priya.nair@harborline.example", "Priya Nair", cancellationToken);
        var marcus = await EnsureUserAsync("marcus.lee@harborline.example", "Marcus Lee", cancellationToken);
        var dev = await EnsureUserAsync("dev.patel@harborline.example", "Dev Patel", cancellationToken);
        var lena = await EnsureUserAsync("lena.cho@harborline.example", "Lena Cho", cancellationToken);
        var auditor = await EnsureUserAsync("auditor@northgate.example", "Rachel Ortiz", cancellationToken);
        var northwindOwner = await EnsureUserAsync("owner@northwind.example", "Alex Kim", cancellationToken);

        await EnsureMembershipAsync(chandra, acme.Id, TenantRole.Admin, cancellationToken);
        await EnsureMembershipAsync(priya, acme.Id, TenantRole.ControlOwner, cancellationToken);
        await EnsureMembershipAsync(marcus, acme.Id, TenantRole.ControlOwner, cancellationToken);
        await EnsureMembershipAsync(dev, acme.Id, TenantRole.Employee, cancellationToken);
        await EnsureMembershipAsync(lena, acme.Id, TenantRole.ControlOwner, cancellationToken);
        await EnsureMembershipAsync(auditor, acme.Id, TenantRole.Auditor, cancellationToken);
        await EnsureMembershipAsync(northwindOwner, northwind.Id, TenantRole.Admin, cancellationToken);
        await EnsureGrantAsync(auditor.Id, acme.Id, cancellationToken);
        await EnsureSsoDomainAsync(acme.Id, cancellationToken);
        await EnsureExamplesAndCollectAsync(acme, "acme", cancellationToken);
        await EnsureExamplesAndCollectAsync(northwind, "northwind", cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var evidence = scope.ServiceProvider.GetRequiredService<EvidenceWriter>();
        var now = time.GetUtcNow();
        await DemoPortal.SeedAsync(databases, acme, now, evidence, cancellationToken);
        await SeedActivityAsync(acme.Id, now, cancellationToken);
    }

    private async Task SeedActivityAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await control.Activity.AnyAsync(item => item.TenantId == tenantId && item.Action == "access.started", cancellationToken))
        {
            return;
        }

        var emails = ClientSeed.Activity.Select(item => item.Actor).Distinct().ToArray();
        var users = await control.Users.Where(user => emails.Contains(user.Email)).ToListAsync(cancellationToken);
        foreach (var item in ClientSeed.Activity)
        {
            var actor = users.FirstOrDefault(user => user.Email == item.Actor);
            control.Activity.Add(new ActivityEvent
            {
                Id = Guid.NewGuid(),
                At = ClientSeed.Moment(now, item.DayOffset),
                ActorUserId = actor?.Id,
                TenantId = tenantId,
                Action = item.Action,
                Result = item.Result,
                Detail = item.Detail,
            });
        }

        await control.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureExamplesAndCollectAsync(Tenant tenant, string fixture, CancellationToken cancellationToken)
    {
        var created = await EnsureExampleIntegrationsAsync(tenant, fixture, cancellationToken);
        if (!created && await HasChainAsync(tenant, cancellationToken))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CollectionRunner>().RunFreshAsync(tenant.Id, cancellationToken);
    }

    private async Task<bool> EnsureExampleIntegrationsAsync(Tenant tenant, string fixture, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName!);
        if (await db.Integrations.AnyAsync(cancellationToken))
        {
            return false;
        }

        foreach (var provider in new[] { "aws", "google", "microsoft", "github" })
        {
            db.Integrations.Add(new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                DisplayName = IntegrationPolicies.DisplayName(provider),
                Status = "connected",
                Example = true,
                Fixture = fixture,
                AccountLabel = fixture == "northwind" ? "Northwind Labs example" : ClientSeed.Company.Name + " example",
                SetupHint = "Example fixture. No customer account is connected.",
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> HasChainAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName!);
        return await db.Chain.AnyAsync(cancellationToken);
    }

    private async Task EnsureNorthwindMarkerAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await using var db = databases.Create(tenant.DatabaseName!);
        if (await db.Controls.AnyAsync(controlItem => controlItem.Code == "NW-01", cancellationToken))
        {
            return;
        }

        db.Controls.Add(new Control
        {
            Id = Guid.NewGuid(),
            Code = "NW-01",
            Title = "Northwind confidential payroll access review",
            Description = "Example control that exists only in the Northwind Labs workspace.",
            Status = ControlStatus.Passing,
            Frequency = "Annual",
            OwnerName = "Alex Kim",
            OwnerEmail = "owner@northwind.example",
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAuditAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await control.Tenants.FirstAsync(item => item.Id == tenantId, cancellationToken);
        await using var db = databases.Create(tenant.DatabaseName!);
        if (await db.Audits.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Audits.Add(new AuditEngagement
        {
            Id = Guid.NewGuid(),
            Name = "SOC 2 Type II — Fieldwork",
            FirmName = "Northgate Assurance LLP",
            FrameworkKey = "soc2",
            FieldworkOn = new DateOnly(2026, 11, 30),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<UserAccount> EnsureUserAsync(string email, string name, CancellationToken cancellationToken)
    {
        var existing = await control.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = name,
            Subject = "local:" + Guid.NewGuid().ToString("N"),
            CreatedAt = time.GetUtcNow(),
        };
        user.PasswordHash = passwords.HashPassword(user, DemoLocalPassword.Value);
        control.Users.Add(user);
        await control.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task EnsureMembershipAsync(UserAccount user, Guid tenantId, TenantRole role, CancellationToken cancellationToken)
    {
        var existing = await control.Memberships.FirstOrDefaultAsync(
            membership => membership.UserId == user.Id && membership.TenantId == tenantId,
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        control.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            Role = role,
        });
        await control.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureGrantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (await control.AuditorGrants.AnyAsync(grant => grant.UserId == userId && grant.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        var grant = new AuditorGrant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            AuditName = "SOC 2 Type II",
            FirmName = "Northgate Assurance LLP",
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            AccessExpiresOn = new DateOnly(2026, 12, 15),
        };
        grant.Frameworks.Add(new AuditorGrantFramework { FrameworkKey = "soc2" });
        control.AuditorGrants.Add(grant);
        await control.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSsoDomainAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var organizationId = Environment.GetEnvironmentVariable("WORKOS_ORGANIZATION_ID");
        var domain = Environment.GetEnvironmentVariable("WORKOS_SSO_DOMAIN");
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var normalized = domain.Trim().ToLowerInvariant();
        var row = await control.TenantDomains.FirstOrDefaultAsync(item => item.Domain == normalized, cancellationToken);
        if (row is null)
        {
            control.TenantDomains.Add(new TenantDomain
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Domain = normalized,
                WorkOsOrganizationId = organizationId.Trim(),
            });
        }
        else
        {
            row.WorkOsOrganizationId = organizationId.Trim();
            row.TenantId = tenantId;
        }

        await control.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Tenant> RequiredTenantAsync(ITenantProvisioner provisioner, CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await provisioner.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException(result.Error ?? "invalid_tenant");
        }

        return result.Value;
    }
}
