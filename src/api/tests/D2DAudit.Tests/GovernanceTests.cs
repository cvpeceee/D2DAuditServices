using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Tests;

public sealed class GovernanceFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public Guid TenantId { get; private set; }

    private PostgresServer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresServer.StartAsync();
        Factory = new ApiFactory(adminConnection: _postgres.ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<UserAccount>>();
        var created = await provisioner.CreateAsync(new CreateTenantRequest("acme-gov", "Acme Gov", "US", true), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);
        var tenant = created.Value!;
        TenantId = tenant.Id;
        await provisioner.SeedExampleControlsAsync(tenant.Id, CancellationToken.None);
        var admin = User(control, hasher, "gov-admin@example.com");
        var auditor = User(control, hasher, "gov-auditor@example.com");
        control.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = admin.Id, TenantId = tenant.Id, Role = TenantRole.Admin });
        control.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = auditor.Id, TenantId = tenant.Id, Role = TenantRole.Auditor });
        var grant = new AuditorGrant
        {
            Id = Guid.NewGuid(),
            UserId = auditor.Id,
            TenantId = tenant.Id,
            AuditName = "ISO 27001",
            FirmName = "Northgate",
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            AccessExpiresOn = new DateOnly(2026, 12, 15),
        };
        grant.Frameworks.Add(new AuditorGrantFramework { FrameworkKey = "soc2" });
        control.AuditorGrants.Add(grant);
        await control.SaveChangesAsync();
        await using var db = databases.Create(tenant.DatabaseName!);
        db.Resources.Add(Snapshot("google", "sam", "Sam Leaved", "sam.leaved@acme.example", offboarded: true, active: false));
        db.Resources.Add(Snapshot("github", "sam-gh", "Sam Leaved", "sam.leaved@acme.example", offboarded: false, active: true));
        db.Resources.Add(Snapshot("microsoft", "jordan", "Jordan P.", "jordan@acme.example", offboarded: false, active: true));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { email, password = "test-password" });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null, Guid? tenantId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-D2D-Request", "1");
        if (tenantId is Guid id)
        {
            request.Headers.Add("X-Tenant-Id", id.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private static UserAccount User(ControlPlaneDbContext db, PasswordHasher<UserAccount> hasher, string email)
    {
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = email,
            Subject = "local:" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, "test-password");
        db.Users.Add(user);
        return user;
    }

    private static ResourceSnapshot Snapshot(string provider, string id, string name, string email, bool offboarded, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        Kind = "user",
        ExternalId = id,
        Name = name,
        AttributesJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["email"] = email,
            ["offboarded"] = offboarded ? "true" : "false",
            ["status"] = active ? "active" : "suspended",
        }),
        CollectedAt = DateTimeOffset.UtcNow,
    };
}

public sealed class GovernanceTests(GovernanceFixture fixture) : IClassFixture<GovernanceFixture>
{
    [Fact]
    public async Task Statement_of_applicability_lists_every_annex_control_for_an_auditor()
    {
        var auditor = await fixture.LoginAsync("gov-auditor@example.com");
        var response = await GovernanceFixture.SendAsync(auditor, HttpMethod.Get, "/api/v1/soa", tenantId: fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<List<SoaRow>>();
        Assert.NotNull(rows);
        Assert.Equal(93, rows.Count);
        Assert.Contains(rows, row => row.Code == "A.5.1");
        Assert.Contains(rows, row => row.Code == "A.8.34");
        var excluded = rows.Single(row => row.Code == "A.7.5");
        Assert.False(excluded.Applicable);
        Assert.False(string.IsNullOrWhiteSpace(excluded.Justification));
        var denied = await GovernanceFixture.SendAsync(auditor, HttpMethod.Put, "/api/v1/soa/A.5.1", new { applicable = false, justification = "no", implementation = "NotApplicable" }, fixture.TenantId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var pdf = await GovernanceFixture.SendAsync(auditor, HttpMethod.Get, "/api/v1/soa/export?format=pdf", tenantId: fixture.TenantId);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-"u8.ToArray(), bytes.Take(5).ToArray());
        Assert.Contains("A.8.34", Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_flags_a_leaver_who_still_has_access()
    {
        var admin = await fixture.LoginAsync("gov-admin@example.com");
        var synced = await GovernanceFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/people/sync", new { }, fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);
        var people = await (await GovernanceFixture.SendAsync(admin, HttpMethod.Get, "/api/v1/people", tenantId: fixture.TenantId)).Content.ReadFromJsonAsync<PersonPage>();
        var sam = Assert.Single(people!.Items, person => person.Email == "sam.leaved@acme.example");
        Assert.Equal("Offboarded", sam.State);
        Assert.True(sam.LeaverStillHasAccess);
        var detail = await (await GovernanceFixture.SendAsync(admin, HttpMethod.Get, "/api/v1/people/" + sam.Id, tenantId: fixture.TenantId)).Content.ReadFromJsonAsync<PersonDetail>();
        Assert.Contains(detail!.Accounts, account => account.Provider == "github" && account.Active);
    }

    [Fact]
    public async Task Completing_a_review_stores_hashed_evidence()
    {
        var admin = await fixture.LoginAsync("gov-admin@example.com");
        var started = await GovernanceFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/access-reviews", new { name = "October review", dueOn = "2026-11-01" }, fixture.TenantId);
        var campaign = await started.Content.ReadFromJsonAsync<AccessCampaignDetail>();
        Assert.NotNull(campaign);
        Assert.NotEmpty(campaign.Items);
        foreach (var item in campaign.Items)
        {
            var decision = item.PersonEmail.Contains("sam", StringComparison.Ordinal) ? "Revoked" : "Approved";
            var decided = await GovernanceFixture.SendAsync(admin, HttpMethod.Post, $"/api/v1/access-reviews/{campaign.Id}/items/{item.Id}", new { decision }, fixture.TenantId);
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        }

        var completed = await GovernanceFixture.SendAsync(admin, HttpMethod.Post, $"/api/v1/access-reviews/{campaign.Id}/complete", tenantId: fixture.TenantId);
        var done = await completed.Content.ReadFromJsonAsync<AccessCampaignDetail>();
        Assert.Equal("Completed", done!.State);
        Assert.False(string.IsNullOrWhiteSpace(done.EvidenceSha256));

        using var scope = fixture.Factory.Services.CreateScope();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageProvider>();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var tenant = await control.Tenants.SingleAsync(item => item.Id == fixture.TenantId);
        await using var db = databases.Create(tenant.DatabaseName!);
        var evidence = await db.Evidence.SingleAsync(item => item.Id == done.EvidenceId);
        var body = await storage.GetAsync(evidence.ObjectKey!, CancellationToken.None);
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        Assert.Equal(evidence.Sha256, hash);
        Assert.Equal(done.EvidenceSha256, hash);
        Assert.Contains("sam.leaved@acme.example", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_risk_outside_the_scale_is_rejected_and_a_due_review_shows_on_the_dashboard()
    {
        var admin = await fixture.LoginAsync("gov-admin@example.com");
        var rejected = await GovernanceFixture.SendAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/risks",
            new
            {
                title = "Too hot",
                statement = "Invalid score",
                likelihood = 9,
                impact = 5,
                ownerName = "Priya",
                ownerEmail = "priya@acme.example",
                treatment = "Mitigate",
                treatmentPlan = "No",
                reviewOn = "2026-10-01",
                residualLikelihood = 1,
                residualImpact = 1,
                controlCodes = Array.Empty<string>(),
            },
            fixture.TenantId);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var saved = await GovernanceFixture.SendAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/risks",
            new
            {
                title = "Overdue review",
                statement = "The review date has passed.",
                likelihood = 3,
                impact = 4,
                ownerName = "Priya",
                ownerEmail = "priya@acme.example",
                treatment = "Accept",
                treatmentPlan = "Reviewed and accepted for the example workspace.",
                reviewOn = "2026-10-01",
                residualLikelihood = 2,
                residualImpact = 2,
                controlCodes = new[] { "AC-07" },
            },
            fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dashboard = await (await GovernanceFixture.SendAsync(admin, HttpMethod.Get, "/api/v1/dashboard", tenantId: fixture.TenantId)).Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.Contains(dashboard!.DueItems, item => item.Kind == "risk" && item.Title == "Overdue review" && item.Overdue);
        Assert.NotEmpty(dashboard.Frameworks);
        Assert.True(dashboard.Frameworks.All(item => item.Percent >= 0));
    }
}
