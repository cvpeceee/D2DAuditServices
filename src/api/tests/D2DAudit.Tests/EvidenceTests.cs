using System.Net;
using System.Net.Http.Json;
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

public sealed class EvidenceFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public Guid AcmeId { get; private set; }

    public Guid NorthwindId { get; private set; }

    public Guid RealId { get; private set; }

    private PostgresServer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = await PostgresServer.StartAsync();
        Factory = new ApiFactory(adminConnection: _postgres.ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<UserAccount>>();
        var acme = await CreatedAsync(provisioner, new CreateTenantRequest("acme", "Acme Robotics", "US", true));
        var northwind = await CreatedAsync(provisioner, new CreateTenantRequest("northwind", "Northwind Labs", "EU", true));
        var real = await CreatedAsync(provisioner, new CreateTenantRequest("realco", "Real Co", "US", false));
        AcmeId = acme.Id;
        NorthwindId = northwind.Id;
        RealId = real.Id;
        await provisioner.SeedExampleControlsAsync(acme.Id, CancellationToken.None);
        await provisioner.SeedExampleControlsAsync(northwind.Id, CancellationToken.None);
        var acmeAdmin = User(control, hasher, "acme-admin@example.com");
        var northwindAdmin = User(control, hasher, "northwind-admin@example.com");
        var employee = User(control, hasher, "employee@example.com");
        var realAdmin = User(control, hasher, "real-admin@example.com");
        Member(control, acmeAdmin, acme.Id, TenantRole.Admin);
        Member(control, northwindAdmin, northwind.Id, TenantRole.Admin);
        Member(control, employee, acme.Id, TenantRole.Employee);
        Member(control, realAdmin, real.Id, TenantRole.Admin);
        await control.SaveChangesAsync();
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

    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Client();
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

    private static async Task<Tenant> CreatedAsync(ITenantProvisioner provisioner, CreateTenantRequest request)
    {
        var result = await provisioner.CreateAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
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

    private static void Member(ControlPlaneDbContext db, UserAccount user, Guid tenantId, TenantRole role) =>
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenantId, Role = role });
}

public sealed class EvidenceTests(EvidenceFixture fixture) : IClassFixture<EvidenceFixture>
{
    [Fact]
    public async Task Example_collection_is_isolated_and_the_chain_detects_tampering()
    {
        var acme = await fixture.LoginAsync("acme-admin@example.com");
        var northwind = await fixture.LoginAsync("northwind-admin@example.com");
        var connected = await EvidenceFixture.SendAsync(acme, HttpMethod.Post, "/api/v1/integrations/example", new { fixture = "acme" }, fixture.AcmeId);
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        var otherConnected = await EvidenceFixture.SendAsync(northwind, HttpMethod.Post, "/api/v1/integrations/example", new { fixture = "northwind" }, fixture.NorthwindId);
        Assert.Equal(HttpStatusCode.OK, otherConnected.StatusCode);

        await CollectAsync(acme, fixture.AcmeId);
        await CollectAsync(northwind, fixture.NorthwindId);

        var acmeTests = await ReadAsync(acme, "/api/v1/tests", fixture.AcmeId);
        var northwindTests = await ReadAsync(northwind, "/api/v1/tests", fixture.NorthwindId);
        Assert.Contains("marketing-site", acmeTests, StringComparison.Ordinal);
        Assert.DoesNotContain("marketing-site", northwindTests, StringComparison.Ordinal);
        Assert.Equal("fail", Outcome(acmeTests, "s3-public-access"));
        Assert.Equal("pass", Outcome(northwindTests, "s3-public-access"));
        Assert.Equal("fail", Outcome(acmeTests, "offboarded-removed"));
        Assert.Equal("pass", Outcome(northwindTests, "offboarded-removed"));

        var acmeEvidence = await ReadAsync(acme, "/api/v1/evidence", fixture.AcmeId);
        var northwindEvidence = await ReadAsync(northwind, "/api/v1/evidence", fixture.NorthwindId);
        Assert.Contains(fixture.AcmeId.ToString("N") + "/", acmeEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.NorthwindId.ToString("N") + "/", acmeEvidence, StringComparison.Ordinal);
        Assert.Contains(fixture.NorthwindId.ToString("N") + "/", northwindEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.AcmeId.ToString("N") + "/", northwindEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain("marketing-site", northwindEvidence, StringComparison.Ordinal);

        var intact = await EvidenceFixture.SendAsync(acme, HttpMethod.Post, "/api/v1/evidence/verify", tenantId: fixture.AcmeId);
        var intactBody = await intact.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, intact.StatusCode);
        Assert.Contains("\"intact\":true", intactBody, StringComparison.Ordinal);

        var file = Directory.GetFiles(fixture.Factory.StorageRoot, "*.parquet", SearchOption.AllDirectories)
            .First(path => path.Contains(fixture.AcmeId.ToString("N"), StringComparison.Ordinal));
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        var broken = await EvidenceFixture.SendAsync(acme, HttpMethod.Post, "/api/v1/evidence/verify", tenantId: fixture.AcmeId);
        var brokenBody = await broken.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, broken.StatusCode);
        Assert.Contains("\"intact\":false", brokenBody, StringComparison.Ordinal);
        Assert.Contains("content_hash", brokenBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employees_cannot_collect_and_real_tenants_cannot_use_fixtures()
    {
        var employee = await fixture.LoginAsync("employee@example.com");
        var denied = await EvidenceFixture.SendAsync(employee, HttpMethod.Post, "/api/v1/integrations/collect", tenantId: fixture.AcmeId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var real = await fixture.LoginAsync("real-admin@example.com");
        var fixtureDenied = await EvidenceFixture.SendAsync(real, HttpMethod.Post, "/api/v1/integrations/example", new { fixture = "acme" }, fixture.RealId);
        Assert.Equal(HttpStatusCode.Forbidden, fixtureDenied.StatusCode);
        var body = await fixtureDenied.Content.ReadAsStringAsync();
        Assert.Contains("forbidden", body, StringComparison.Ordinal);
        Assert.DoesNotContain("marketing-site", body, StringComparison.Ordinal);
    }

    private static async Task CollectAsync(HttpClient client, Guid tenantId)
    {
        var started = await EvidenceFixture.SendAsync(client, HttpMethod.Post, "/api/v1/integrations/collect", tenantId: tenantId);
        var startedBody = await started.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var jobId = JsonSerializer.Deserialize<Guid>(startedBody);
        string? status = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var jobs = await EvidenceFixture.SendAsync(client, HttpMethod.Get, "/api/v1/jobs", tenantId: tenantId);
            var document = JsonDocument.Parse(await jobs.Content.ReadAsStringAsync());
            foreach (var job in document.RootElement.EnumerateArray())
            {
                if (job.GetProperty("id").GetGuid() != jobId)
                {
                    continue;
                }

                status = job.GetProperty("status").GetString();
            }

            if (status is "succeeded" or "failed")
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.Equal("succeeded", status);
    }

    private static string Outcome(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("key").GetString() == key)
            {
                return item.GetProperty("outcome").GetString() ?? "";
            }
        }

        return "";
    }

    private static async Task<string> ReadAsync(HttpClient client, string url, Guid tenantId)
    {
        var response = await EvidenceFixture.SendAsync(client, HttpMethod.Get, url, tenantId: tenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
