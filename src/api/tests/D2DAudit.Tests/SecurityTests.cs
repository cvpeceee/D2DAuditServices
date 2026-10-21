using System.Net;
using System.Net.Http.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Tests;

public sealed class SecurityFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();

    public Guid AcmeId { get; private set; }

    public Guid OtherId { get; private set; }

    public const string Password = "test-password";

    public const string AcmeMarker = "Acme only marker control";

    public const string OtherMarker = "Other tenant secret control";

    public const string IsoOnly = "Privacy notice kept current for Acme";

    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<UserAccount>>();

        var acme = await provisioner.CreateAsync(new CreateTenantRequest("acme", "Acme Robotics", "US", true), CancellationToken.None);
        var other = await provisioner.CreateAsync(new CreateTenantRequest("other", "Other Co", "EU", false), CancellationToken.None);
        AcmeId = acme.Id;
        OtherId = other.Id;

        await AddControlAsync(databases, acme.DatabaseName!, "AC-ONLY", AcmeMarker, "soc2", "CC6.1");
        await AddControlAsync(databases, acme.DatabaseName!, "PP-01", IsoOnly, "iso27001", "A.5.34");
        await AddControlAsync(databases, other.DatabaseName!, "OT-01", OtherMarker, null, null);

        var acmeAdmin = await UserAsync(control, hasher, "acme-admin@example.com");
        var otherAdmin = await UserAsync(control, hasher, "other-admin@example.com");
        var employee = await UserAsync(control, hasher, "employee@example.com");
        var auditor = await UserAsync(control, hasher, "auditor@example.com");
        var expired = await UserAsync(control, hasher, "expired@example.com");
        var both = await UserAsync(control, hasher, "both@example.com");
        Member(control, acmeAdmin, acme.Id, TenantRole.Admin);
        Member(control, otherAdmin, other.Id, TenantRole.Admin);
        Member(control, employee, acme.Id, TenantRole.Employee);
        Member(control, auditor, acme.Id, TenantRole.Auditor);
        Member(control, expired, acme.Id, TenantRole.Auditor);
        Member(control, both, acme.Id, TenantRole.Admin);
        Member(control, both, other.Id, TenantRole.Admin);
        Grant(control, auditor, acme.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), "soc2");
        Grant(control, expired, acme.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), "soc2");
        await control.SaveChangesAsync();
    }

    public Task DisposeAsync() => Factory.DisposeAsync().AsTask();

    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Client();
        var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { email, password = Password });
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

    private static async Task AddControlAsync(TenantDatabases databases, string databaseName, string code, string title, string? framework, string? requirement)
    {
        await using var db = databases.Create(databaseName);
        var control = new Control
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title,
            Description = title,
            Status = ControlStatus.Passing,
            Frequency = "Annual",
        };
        db.Controls.Add(control);
        if (framework is not null && requirement is not null)
        {
            var match = await db.Requirements.Include(item => item.Framework)
                .FirstAsync(item => item.Framework!.Key == framework && item.Code == requirement);
            db.ControlMappings.Add(new ControlRequirementMap
            {
                ControlId = control.Id,
                RequirementId = match.Id,
                Coverage = MappingCoverage.Full,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<UserAccount> UserAsync(ControlPlaneDbContext db, PasswordHasher<UserAccount> hasher, string email)
    {
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = email,
            Subject = "local:" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static void Member(ControlPlaneDbContext db, UserAccount user, Guid tenantId, TenantRole role) =>
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenantId, Role = role });

    private static void Grant(ControlPlaneDbContext db, UserAccount user, Guid tenantId, DateOnly expires, string framework)
    {
        var grant = new AuditorGrant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            AuditName = "Test audit",
            FirmName = "Test firm",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 6, 30),
            AccessExpiresOn = expires,
        };
        grant.Frameworks.Add(new AuditorGrantFramework { FrameworkKey = framework });
        db.AuditorGrants.Add(grant);
    }
}

public sealed class SecurityTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Fact]
    public async Task User_cannot_read_another_tenants_controls()
    {
        var client = await fixture.LoginAsync("acme-admin@example.com");
        var response = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.OtherId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(SecurityFixture.OtherMarker, body);
        Assert.DoesNotContain(SecurityFixture.AcmeMarker, body);
    }

    [Fact]
    public async Task User_reads_only_their_tenant()
    {
        var client = await fixture.LoginAsync("acme-admin@example.com");
        var response = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SecurityFixture.AcmeMarker, body);
        Assert.DoesNotContain(SecurityFixture.OtherMarker, body);
    }

    [Fact]
    public async Task Other_tenant_admin_cannot_read_acme()
    {
        var client = await fixture.LoginAsync("other-admin@example.com");
        var denied = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        var allowed = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.OtherId);
        var allowedBody = await allowed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain(SecurityFixture.AcmeMarker, deniedBody);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(SecurityFixture.OtherMarker, allowedBody);
        Assert.DoesNotContain(SecurityFixture.AcmeMarker, allowedBody);
    }

    [Fact]
    public async Task Unknown_tenant_is_forbidden_without_leaking_data()
    {
        var client = await fixture.LoginAsync("acme-admin@example.com");
        var response = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(SecurityFixture.AcmeMarker, body);
        Assert.DoesNotContain(SecurityFixture.OtherMarker, body);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await SecurityFixture.SendAsync(fixture.Client(), HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auditor_is_read_only_and_scoped_to_the_granted_framework()
    {
        var client = await fixture.LoginAsync("auditor@example.com");
        var frameworks = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/frameworks", tenantId: fixture.AcmeId);
        var frameworkBody = await frameworks.Content.ReadAsStringAsync();
        var iso = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/frameworks/iso27001", tenantId: fixture.AcmeId);
        var controls = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        var controlBody = await controls.Content.ReadAsStringAsync();
        var hidden = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls/PP-01", tenantId: fixture.AcmeId);
        var write = await SecurityFixture.SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/controls/AC-ONLY/owner",
            new { ownerName = "Auditor", ownerEmail = "auditor@example.com" },
            fixture.AcmeId);

        Assert.Equal(HttpStatusCode.OK, frameworks.StatusCode);
        Assert.Contains("soc2", frameworkBody);
        Assert.DoesNotContain("iso27001", frameworkBody);
        Assert.Equal(HttpStatusCode.NotFound, iso.StatusCode);
        Assert.Contains(SecurityFixture.AcmeMarker, controlBody);
        Assert.DoesNotContain(SecurityFixture.IsoOnly, controlBody);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        var admin = await fixture.LoginAsync("acme-admin@example.com");
        var after = await SecurityFixture.SendAsync(admin, HttpMethod.Get, "/api/v1/controls/AC-ONLY", tenantId: fixture.AcmeId);
        var afterBody = await after.Content.ReadAsStringAsync();
        Assert.DoesNotContain("auditor@example.com", afterBody);
    }

    [Fact]
    public async Task Employee_cannot_change_a_control_and_admin_can()
    {
        var employee = await fixture.LoginAsync("employee@example.com");
        var denied = await SecurityFixture.SendAsync(
            employee,
            HttpMethod.Put,
            "/api/v1/controls/AC-ONLY/owner",
            new { ownerName = "Employee", ownerEmail = "employee@example.com" },
            fixture.AcmeId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var admin = await fixture.LoginAsync("acme-admin@example.com");
        var updated = await SecurityFixture.SendAsync(
            admin,
            HttpMethod.Put,
            "/api/v1/controls/AC-ONLY/owner",
            new { ownerName = "Priya N.", ownerEmail = "priya@example.com" },
            fixture.AcmeId);
        var body = await updated.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Contains("priya@example.com", body);
    }

    [Fact]
    public async Task Expired_auditor_grant_loses_access()
    {
        var client = await fixture.LoginAsync("expired@example.com");
        var response = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(SecurityFixture.AcmeMarker, body);
    }

    [Fact]
    public async Task Multiple_memberships_require_an_explicit_tenant()
    {
        var client = await fixture.LoginAsync("both@example.com");
        var missing = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var acme = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.AcmeId);
        var other = await SecurityFixture.SendAsync(client, HttpMethod.Get, "/api/v1/controls", tenantId: fixture.OtherId);
        Assert.Contains(SecurityFixture.AcmeMarker, await acme.Content.ReadAsStringAsync());
        Assert.Contains(SecurityFixture.OtherMarker, await other.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        var response = await SecurityFixture.SendAsync(
            fixture.Client(),
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { email = "acme-admin@example.com", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
