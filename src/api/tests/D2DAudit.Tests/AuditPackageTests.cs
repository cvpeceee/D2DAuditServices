using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
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

public sealed class AuditPackageFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public Guid TenantId { get; private set; }

    public Guid OtherId { get; private set; }

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
        var tenant = await CreatedAsync(provisioner, new CreateTenantRequest("acme-audit", "Acme Audit", "US", true));
        var other = await CreatedAsync(provisioner, new CreateTenantRequest("other-audit", "Other Audit", "EU", true));
        TenantId = tenant.Id;
        OtherId = other.Id;
        await provisioner.SeedExampleControlsAsync(tenant.Id, CancellationToken.None);
        var admin = User(control, hasher, "audit-admin@example.com");
        var employee = User(control, hasher, "audit-employee@example.com");
        var auditor = User(control, hasher, "audit-auditor@example.com");
        var outsider = User(control, hasher, "audit-other@example.com");
        Member(control, admin, tenant.Id, TenantRole.Admin);
        Member(control, employee, tenant.Id, TenantRole.Employee);
        Member(control, auditor, tenant.Id, TenantRole.Auditor);
        Member(control, outsider, other.Id, TenantRole.Admin);
        Grant(control, auditor, tenant.Id);
        await control.SaveChangesAsync();
        await using var db = databases.Create(tenant.DatabaseName!);
        db.Audits.Add(new AuditEngagement
        {
            Id = Guid.NewGuid(),
            Name = "SOC 2 Type II audit",
            FirmName = "Northgate Assurance LLP",
            FrameworkKey = "soc2",
            Scope = "Security and Availability",
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            FieldworkOn = new DateOnly(2026, 11, 30),
        });
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

    private static void Grant(ControlPlaneDbContext db, UserAccount user, Guid tenantId)
    {
        var grant = new AuditorGrant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            AuditName = "SOC 2",
            FirmName = "Northgate",
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            AccessExpiresOn = new DateOnly(2026, 12, 15),
        };
        grant.Frameworks.Add(new AuditorGrantFramework { FrameworkKey = "soc2" });
        db.AuditorGrants.Add(grant);
    }
}

public sealed class AuditPackageTests(AuditPackageFixture fixture) : IClassFixture<AuditPackageFixture>
{
    [Fact]
    public async Task Auditor_can_request_and_cannot_change_customer_records()
    {
        var admin = await fixture.LoginAsync("audit-admin@example.com");
        var auditor = await fixture.LoginAsync("audit-auditor@example.com");
        var employee = await fixture.LoginAsync("audit-employee@example.com");

        var installed = await AuditPackageFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/policies/library", new { }, fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, installed.StatusCode);

        var hidden = await AuditPackageFixture.SendAsync(auditor, HttpMethod.Get, "/api/v1/policies/POL-PRI", tenantId: fixture.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var list = await JsonAsync(auditor, HttpMethod.Get, "/api/v1/policies");
        var codes = list.EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray();
        Assert.Contains("POL-ACC", codes);
        Assert.DoesNotContain("POL-PRI", codes);

        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(auditor, HttpMethod.Post, "/api/v1/policies/POL-ACC/approve", new { }, fixture.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(employee, HttpMethod.Post, "/api/v1/policies/POL-ACC/approve", new { }, fixture.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuditPackageFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/policies/POL-ACC/submit", new { }, fixture.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuditPackageFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/policies/POL-ACC/approve", new { }, fixture.TenantId)).StatusCode);

        var due = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        var assigned = await AuditPackageFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/policies/POL-ACC/acknowledgements", new { personName = "Sam Pending", personEmail = "pending@example.com", dueOn = due }, fixture.TenantId);
        Assert.True(assigned.StatusCode == HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        var mine = await AuditPackageFixture.SendAsync(admin, HttpMethod.Post, "/api/v1/policies/POL-ACC/acknowledgements", new { personName = "Employee", personEmail = "audit-employee@example.com", dueOn = due }, fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuditPackageFixture.SendAsync(employee, HttpMethod.Post, "/api/v1/policies/POL-ACC/acknowledge", new { }, fixture.TenantId)).StatusCode);
        var reminded = await JsonAsync(admin, HttpMethod.Post, "/api/v1/policies/reminders", new { });
        Assert.True(reminded.GetInt32() >= 1);
        var again = await JsonAsync(admin, HttpMethod.Post, "/api/v1/policies/reminders", new { });
        Assert.Equal(0, again.GetInt32());

        var portal = await JsonAsync(auditor, HttpMethod.Get, "/api/v1/audits");
        Assert.Equal("SOC 2 Type II audit", portal.GetProperty("engagement").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(employee, HttpMethod.Post, "/api/v1/audits/requests", new { title = "No", population = "1", sampleSize = 1, source = "Manual", dueOn = due }, fixture.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(employee, HttpMethod.Post, "/api/v1/audits/package", new { }, fixture.TenantId)).StatusCode);

        var created = await JsonAsync(auditor, HttpMethod.Post, "/api/v1/audits/requests", new { title = "New-hire onboarding", population = "23 hires", sampleSize = 5, source = "HR system", dueOn = "2026-10-24" });
        var code = created.GetProperty("code").GetString();
        Assert.StartsWith("REQ-", code);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(auditor, HttpMethod.Post, $"/api/v1/audits/requests/{code}/respond", new { body = "Auditor cannot answer for the customer." }, fixture.TenantId)).StatusCode);
        var submitted = await JsonAsync(admin, HttpMethod.Post, $"/api/v1/audits/requests/{code}/respond", new { body = "The five hire files are ready." });
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        var accepted = await JsonAsync(auditor, HttpMethod.Post, $"/api/v1/audits/requests/{code}/accept", new { });
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());

        var finding = await JsonAsync(auditor, HttpMethod.Post, "/api/v1/audits/findings", new { title = "MFA gap", severity = "Exception", detail = "Three accounts had no second factor.", requirementCode = "CC6.1" });
        var findingCode = finding.GetProperty("code").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await AuditPackageFixture.SendAsync(auditor, HttpMethod.Post, $"/api/v1/audits/findings/{findingCode}/response", new { response = "Auditor cannot write the management response." }, fixture.TenantId)).StatusCode);
        var answered = await JsonAsync(admin, HttpMethod.Post, $"/api/v1/audits/findings/{findingCode}/response", new { response = "The accounts are enrolled." });
        Assert.Equal("The accounts are enrolled.", answered.GetProperty("managementResponse").GetString());

        var activity = await JsonAsync(admin, HttpMethod.Get, "/api/v1/activity");
        var actions = activity.EnumerateArray().Select(item => (item.GetProperty("action").GetString(), item.GetProperty("actorEmail").GetString())).ToArray();
        Assert.Contains(actions, item => item.Item1 == "audit.portal.viewed" && item.Item2 == "audit-auditor@example.com");
        Assert.Contains(actions, item => item.Item1 == "audit.request.created" && item.Item2 == "audit-auditor@example.com");
        Assert.Contains(actions, item => item.Item1 == "audit.request.accepted" && item.Item2 == "audit-auditor@example.com");
        Assert.Contains(actions, item => item.Item1 == "audit.finding.created" && item.Item2 == "audit-auditor@example.com");
        Assert.Contains(actions, item => item.Item1 == "audit_denied" && item.Item2 == "audit-employee@example.com");
    }

    [Fact]
    public async Task Export_matches_hashes_and_stays_inside_the_auditor_scope()
    {
        var admin = await fixture.LoginAsync("audit-admin@example.com");
        var auditor = await fixture.LoginAsync("audit-auditor@example.com");
        var outsider = await fixture.LoginAsync("audit-other@example.com");
        await UploadAsync(admin, "AC-07", "Access review", "access-review");
        await UploadAsync(admin, "PP-01", "Privacy notice", "privacy-notice");

        var auditorZip = await PackageAsync(auditor);
        var auditorNames = auditorZip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains(auditorNames, name => name.Contains("AC-07", StringComparison.Ordinal));
        Assert.DoesNotContain(auditorNames, name => name.Contains("PP-01", StringComparison.Ordinal));
        AssertManifest(auditorZip, intact: true, reason: null);

        var adminZip = await PackageAsync(admin);
        Assert.Contains(adminZip.Entries.Select(entry => entry.FullName), name => name.Contains("PP-01", StringComparison.Ordinal));
        AssertManifest(adminZip, intact: true, reason: null);

        var evidence = await JsonAsync(admin, HttpMethod.Get, "/api/v1/evidence");
        var key = evidence.GetProperty("items").EnumerateArray().First(item => item.GetProperty("title").GetString() == "Access review").GetProperty("objectKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(key));
        await File.WriteAllBytesAsync(Path.Combine(fixture.Factory.StorageRoot, key.Replace('/', Path.DirectorySeparatorChar)), "tampered"u8.ToArray());

        var broken = await PackageAsync(auditor);
        AssertManifest(broken, intact: false, reason: "content_hash");

        var denied = await AuditPackageFixture.SendAsync(outsider, HttpMethod.Get, "/api/v1/audits/packages/" + Guid.NewGuid(), tenantId: fixture.TenantId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<ZipArchive> PackageAsync(HttpClient client)
    {
        var started = await JsonAsync(client, HttpMethod.Post, "/api/v1/audits/package", new { });
        var id = started.GetGuid();
        string? status = null;
        for (var attempt = 0; attempt < 50 && status is not "succeeded" and not "failed"; attempt++)
        {
            await Task.Delay(200);
            var jobs = await JsonAsync(client, HttpMethod.Get, "/api/v1/jobs");
            var job = jobs.EnumerateArray().First(item => item.GetProperty("id").GetGuid() == id);
            status = job.GetProperty("status").GetString();
        }

        Assert.Equal("succeeded", status);
        var response = await AuditPackageFixture.SendAsync(client, HttpMethod.Get, $"/api/v1/audits/packages/{id}", tenantId: fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
        return new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }

    private static void AssertManifest(ZipArchive zip, bool intact, string? reason)
    {
        var manifest = JsonDocument.Parse(Read(zip, "manifest.json"));
        Assert.Equal(intact, manifest.RootElement.GetProperty("intact").GetBoolean());
        Assert.Equal(reason, manifest.RootElement.GetProperty("reason").GetString());
        var report = JsonDocument.Parse(Read(zip, "integrity-report.json"));
        Assert.Equal(intact, report.RootElement.GetProperty("intact").GetBoolean());
        var csv = Read(zip, "control-matrix.csv");
        Assert.Contains("AC-07", csv, StringComparison.Ordinal);
        var pdf = zip.GetEntry("control-matrix.pdf")!.Open();
        using var reader = new BinaryReader(pdf);
        var header = reader.ReadBytes(5);
        Assert.Equal("%PDF-"u8.ToArray(), header);
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            var bytes = ReadBytes(zip, path);
            var hash = EvidenceChain.ContentHash(bytes);
            Assert.Equal(hash, file.GetProperty("sha256").GetString());
            if (intact)
            {
                Assert.Equal(hash, file.GetProperty("recordedSha256").GetString());
            }
        }
    }

    private async Task UploadAsync(HttpClient client, string controlCode, string title, string text)
    {
        var response = await AuditPackageFixture.SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/evidence",
            new { controlCode, title, contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) },
            fixture.TenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> JsonAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var response = await AuditPackageFixture.SendAsync(client, method, url, body, fixture.TenantId);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Read(ZipArchive zip, string name) => Encoding.UTF8.GetString(ReadBytes(zip, name));

    private static byte[] ReadBytes(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
