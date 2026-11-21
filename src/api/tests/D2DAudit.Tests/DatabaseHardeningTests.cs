using System.Net;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace D2DAudit.Tests;

public sealed class DatabaseSettingsTests
{
    [Theory]
    [InlineData("50%_off\\", "50\\%\\_off\\\\")]
    [InlineData("plain", "plain")]
    public void Like_patterns_escape_wildcards(string term, string escaped)
    {
        Assert.Equal(escaped, DbText.EscapeLike(term));
        Assert.Equal($"%{escaped}%", DbText.ContainsPattern(term));
    }

    [Fact]
    public void Tenant_pool_is_capped_and_settings_are_clamped()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CommandTimeoutSeconds"] = "100000",
            ["Database:MaxRetryCount"] = "-4",
            ["Database:TenantMaxPoolSize"] = "15",
        }).Build();
        var settings = DatabaseSettings.From(configuration);

        Assert.Equal(new DatabaseSettings(600, 0, 15), settings);
        var connection = new NpgsqlConnectionStringBuilder(settings.TenantConnectionString("Host=db;Database=d2d;Maximum Pool Size=100;Minimum Pool Size=30", "t_abc"));
        Assert.Equal("t_abc", connection.Database);
        Assert.Equal(15, connection.MaxPoolSize);
        Assert.Equal(0, connection.MinPoolSize);

        var defaults = DatabaseSettings.From(new ConfigurationBuilder().Build());
        Assert.Equal(new DatabaseSettings(30, 3, 20), defaults);
        var small = new NpgsqlConnectionStringBuilder(defaults.TenantConnectionString("Host=db;Maximum Pool Size=5", "t_x"));
        Assert.Equal(5, small.MaxPoolSize);
    }
}

public sealed class DatabaseHardeningTests(EvidenceFixture fixture) : IClassFixture<EvidenceFixture>
{
    [Fact]
    public async Task Evidence_chain_and_activity_log_reject_update_and_delete()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var created = await provisioner.CreateAsync(new CreateTenantRequest("append-only", "Append Only", "US", true), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        await using (var db = databases.Create(created.Value!.DatabaseName!))
        {
            db.Chain.Add(new ChainEntry
            {
                Id = Guid.NewGuid(),
                Sequence = 1,
                ObjectKey = "k",
                ContentHash = new string('a', 64),
                Head = new string('b', 64),
                Canonical = "c",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE evidence_chain SET \"Head\" = 'x'"));
            Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM evidence_chain"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE evidence_chain"));
            Assert.Equal(1, await db.Chain.CountAsync());
        }

        control.Activity.Add(new ActivityEvent { Id = Guid.NewGuid(), At = DateTimeOffset.UtcNow, Action = "test.append_only", Result = "ok" });
        await control.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => control.Database.ExecuteSqlRawAsync("UPDATE activity_events SET \"Result\" = 'tampered'"));
        await Assert.ThrowsAsync<PostgresException>(() => control.Database.ExecuteSqlRawAsync("DELETE FROM activity_events"));
    }

    [Fact]
    public async Task Evidence_download_is_targeted_and_tenant_scoped()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var acmeDatabase = await control.Tenants.Where(item => item.Id == fixture.AcmeId).Select(item => item.DatabaseName!).SingleAsync();
        string controlCode;
        await using (var db = databases.Create(acmeDatabase))
        {
            controlCode = await db.Controls.OrderBy(item => item.Code).Select(item => item.Code).FirstAsync();
        }

        var acme = await fixture.LoginAsync("acme-admin@example.com");
        var upload = await EvidenceFixture.SendAsync(acme, HttpMethod.Post, "/api/v1/evidence", new { controlCode, title = "Board minutes", contentBase64 = Convert.ToBase64String("minutes"u8.ToArray()) }, fixture.AcmeId);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();

        var download = await EvidenceFixture.SendAsync(acme, HttpMethod.Get, $"/api/v1/evidence/{id}/download", tenantId: fixture.AcmeId);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("minutes", await download.Content.ReadAsStringAsync());

        var northwind = await fixture.LoginAsync("northwind-admin@example.com");
        var crossTenant = await EvidenceFixture.SendAsync(northwind, HttpMethod.Get, $"/api/v1/evidence/{id}/download", tenantId: fixture.NorthwindId);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    [Fact]
    public async Task Login_email_match_is_case_insensitive_but_not_a_wildcard()
    {
        var mixedCase = await EvidenceFixture.SendAsync(fixture.Client(), HttpMethod.Post, "/api/v1/auth/login", new { email = "ACME-Admin@Example.com", password = "test-password" });
        Assert.True(mixedCase.StatusCode == HttpStatusCode.OK, await mixedCase.Content.ReadAsStringAsync());

        var wildcard = await EvidenceFixture.SendAsync(fixture.Client(), HttpMethod.Post, "/api/v1/auth/login", new { email = "acme_admin@example.com", password = "test-password" });
        Assert.NotEqual(HttpStatusCode.OK, wildcard.StatusCode);
    }
}
