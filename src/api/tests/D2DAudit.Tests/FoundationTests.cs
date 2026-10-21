using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Catalog;
using D2DAudit.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace D2DAudit.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void Database_names_are_restricted_to_safe_identifiers()
    {
        var name = DatabaseNames.ForTenant(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        Assert.Equal("t_0123456789abcdef0123456789abcdef", name);
        Assert.True(DatabaseNames.IsSafe(name));
        Assert.False(DatabaseNames.IsSafe("t_ok;drop database postgres"));
        Assert.False(DatabaseNames.IsSafe("T_UPPER"));
        Assert.False(DatabaseNames.IsSafe(""));
    }

    [Fact]
    public void Retention_can_only_grow_and_stays_above_the_minimum()
    {
        var tenant = new Tenant { RetentionYears = 3 };
        var grown = tenant.SetRetentionYears(6);
        Assert.True(grown.Succeeded);
        Assert.Equal(6, tenant.RetentionYears);
        Assert.Equal("cannot_shrink", tenant.SetRetentionYears(4).Error);
        Assert.Equal(6, tenant.RetentionYears);
        Assert.Equal("below_minimum", tenant.SetRetentionYears(2).Error);
        Assert.Equal(6, tenant.RetentionYears);
    }

    [Theory]
    [InlineData("Production", null, null)]
    [InlineData("Staging", "", "")]
    [InlineData("Development", "sk_test", null)]
    public void Local_identity_is_rejected_outside_development_or_when_misconfigured(string environment, string? apiKey, string? clientId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IdentityModeResolver.Resolve(environment, apiKey, clientId));
        Assert.Contains("WORKOS", exception.Message);
    }

    [Fact]
    public void WorkOS_is_selected_when_both_credentials_exist()
    {
        Assert.Equal(IdentityMode.WorkOs, IdentityModeResolver.Resolve("Production", "sk_test", "client_test"));
        Assert.Equal(IdentityMode.Local, IdentityModeResolver.Resolve("Development", null, null));
        Assert.Equal(IdentityMode.Local, IdentityModeResolver.Resolve("Testing", null, null));
    }

    [Fact]
    public async Task Production_host_does_not_start_without_WorkOS()
    {
        await using var postgres = await PostgresServer.StartAsync();
        var factory = new ApiFactory("Production", postgres.ConnectionString);
        var rejected = false;
        try
        {
            _ = factory.Services;
        }
        catch (Exception exception)
        {
            rejected = exception.ToString().Contains("WORKOS", StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.True(rejected);
    }

    [Fact]
    public void Framework_content_matches_the_public_structure()
    {
        var catalog = new FrameworkCatalog();
        var iso = catalog.Frameworks.Single(framework => framework.Key == "iso27001");
        var soc = catalog.Frameworks.Single(framework => framework.Key == "soc2");
        var isoLeaves = Leaves(iso.Requirements).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        var socLeaves = Leaves(soc.Requirements).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        var expectedIso = Enumerable.Range(1, 37).Select(i => $"A.5.{i}")
            .Concat(Enumerable.Range(1, 8).Select(i => $"A.6.{i}"))
            .Concat(Enumerable.Range(1, 14).Select(i => $"A.7.{i}"))
            .Concat(Enumerable.Range(1, 34).Select(i => $"A.8.{i}"))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        var expectedSoc = new[]
        {
            "CC1.1", "CC1.2", "CC1.3", "CC1.4", "CC1.5",
            "CC2.1", "CC2.2", "CC2.3",
            "CC3.1", "CC3.2", "CC3.3", "CC3.4",
            "CC4.1", "CC4.2",
            "CC5.1", "CC5.2", "CC5.3",
            "CC6.1", "CC6.2", "CC6.3", "CC6.4", "CC6.5", "CC6.6", "CC6.7", "CC6.8",
            "CC7.1", "CC7.2", "CC7.3", "CC7.4", "CC7.5",
            "CC8.1",
            "CC9.1", "CC9.2",
            "A1.1", "A1.2", "A1.3",
        }.OrderBy(code => code, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedIso, isoLeaves.Select(item => item.Code).ToArray());
        Assert.Equal(expectedSoc, socLeaves.Select(item => item.Code).ToArray());
        Assert.Equal("Policies for information security", isoLeaves.Single(item => item.Code == "A.5.1").Title);
        Assert.Equal("Authentication information", isoLeaves.Single(item => item.Code == "A.5.17").Title);
        Assert.Equal("Secure authentication", isoLeaves.Single(item => item.Code == "A.8.5").Title);
        Assert.Equal("Use of cryptography", isoLeaves.Single(item => item.Code == "A.8.24").Title);
        Assert.Contains("Logical access", socLeaves.Single(item => item.Code == "CC6.1").Title);
        Assert.All(isoLeaves.Concat(socLeaves), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Summary));
            Assert.DoesNotContain("shall", item.Summary, StringComparison.OrdinalIgnoreCase);
        });

        var leaves = catalog.Frameworks.SelectMany(framework => Leaves(framework.Requirements).Select(leaf => (framework.Key, leaf.Code))).ToHashSet();
        Assert.Contains(catalog.ExampleControls.Controls, control => control.Code == "PP-01");
        Assert.All(catalog.ExampleControls.Controls, control =>
        {
            Assert.NotEmpty(control.Mappings);
            Assert.All(control.Mappings, mapping => Assert.Contains((mapping.Framework, mapping.Requirement), leaves));
        });
        Assert.All(
            catalog.ExampleControls.Controls.Single(control => control.Code == "PP-01").Mappings,
            mapping => Assert.Equal(("iso27001", "A.5.34"), (mapping.Framework, mapping.Requirement)));
    }

    [Fact]
    public async Task Local_tokens_are_rejected_when_they_are_expired_or_retargeted()
    {
        var key = DevOnlySigningKey.Value;
        var valid = SignedTokenValidator.CreateLocalToken(key, "local:1", "a@example.com", "A", DateTime.UtcNow.AddMinutes(5));
        var identity = await SignedTokenValidator.ValidateAsync(valid, SignedTokenValidator.LocalParameters(key));
        Assert.Equal("local:1", identity?.Subject);

        var expired = SignedTokenValidator.CreateLocalToken(key, "local:1", "a@example.com", "A", DateTime.UtcNow.AddMinutes(-10));
        Assert.Null(await SignedTokenValidator.ValidateAsync(expired, SignedTokenValidator.LocalParameters(key)));

        var otherAudience = new TokenValidationParameters
        {
            ValidIssuer = LocalTokenConstants.Issuer,
            ValidAudience = "someone-else",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
        Assert.Null(await SignedTokenValidator.ValidateAsync(valid, otherAudience));
        Assert.Null(await SignedTokenValidator.ValidateAsync(UnsignedToken(), SignedTokenValidator.LocalParameters(key)));
    }

    [Fact]
    public async Task WorkOS_tokens_must_match_issuer_client_and_signature()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        var parameters = SignedTokenValidator.WorkOsParameters([key], "https://api.workos.com", audience: null);
        var token = WorkOsToken(key, "https://api.workos.com", "client_test", DateTime.UtcNow.AddMinutes(5), audience: null);
        var identity = await SignedTokenValidator.ValidateAsync(token, parameters, "client_test");
        Assert.Equal("user_test", identity?.Subject);
        Assert.Null(await SignedTokenValidator.ValidateAsync(token, parameters, "client_other"));

        var expired = WorkOsToken(key, "https://api.workos.com", "client_test", DateTime.UtcNow.AddMinutes(-10), audience: null);
        Assert.Null(await SignedTokenValidator.ValidateAsync(expired, parameters, "client_test"));

        var wrongIssuer = WorkOsToken(key, "https://evil.example", "client_test", DateTime.UtcNow.AddMinutes(5), audience: null);
        Assert.Null(await SignedTokenValidator.ValidateAsync(wrongIssuer, parameters, "client_test"));

        var withAudience = SignedTokenValidator.WorkOsParameters([key], "https://api.workos.com", "https://api.d2d.example");
        var missingAudience = WorkOsToken(key, "https://api.workos.com", "client_test", DateTime.UtcNow.AddMinutes(5), audience: null);
        Assert.Null(await SignedTokenValidator.ValidateAsync(missingAudience, withAudience, "client_test"));
        var audienceToken = WorkOsToken(key, "https://api.workos.com", "client_test", DateTime.UtcNow.AddMinutes(5), "https://api.d2d.example");
        Assert.NotNull(await SignedTokenValidator.ValidateAsync(audienceToken, withAudience, "client_test"));
        Assert.Null(await SignedTokenValidator.ValidateAsync(UnsignedToken(), parameters, "client_test"));
    }

    private static string WorkOsToken(RsaSecurityKey key, string issuer, string clientId, DateTime expires, string? audience)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user_test",
                ["client_id"] = clientId,
            },
        });
    }

    private static string UnsignedToken()
    {
        static string Encode(object value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var header = Encode(new { alg = "none", typ = "JWT" });
        var payload = Encode(new { sub = "user_test", exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() });
        return header + "." + payload + ".";
    }

    private static IEnumerable<RequirementNode> Leaves(IEnumerable<RequirementNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count == 0)
            {
                yield return node;
            }
            else
            {
                foreach (var child in Leaves(node.Children))
                {
                    yield return child;
                }
            }
        }
    }
}
