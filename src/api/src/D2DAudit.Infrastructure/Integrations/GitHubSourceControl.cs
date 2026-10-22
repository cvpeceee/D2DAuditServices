using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class GitHubSourceControl(HttpClient http, IConfiguration configuration, ILogger<GitHubSourceControl> logger) : ISourceControlProvider
{
    public string ProviderKey => "github";

    public Task<Result<ProviderConnect>> ConnectAsync(
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        CancellationToken cancellationToken)
    {
        var state = request.State ?? existing?.ExternalId ?? Guid.NewGuid().ToString("N");
        var installation = string.IsNullOrWhiteSpace(request.InstallationId) ? existing?.InstallationId : request.InstallationId.Trim();
        if (existing?.Example == true)
        {
            return Task.FromResult(Result<ProviderConnect>.Ok(Describe(state, installation)));
        }

        if (string.IsNullOrWhiteSpace(installation))
        {
            return Task.FromResult(Result<ProviderConnect>.Ok(Describe(state, null)));
        }

        if (!installation.All(char.IsAsciiDigit))
        {
            return Task.FromResult(Result<ProviderConnect>.Fail("invalid_installation"));
        }

        if (string.IsNullOrWhiteSpace(configuration["GITHUB_APP_ID"]) || string.IsNullOrWhiteSpace(configuration["GITHUB_APP_PRIVATE_KEY"]))
        {
            return Task.FromResult(Result<ProviderConnect>.Fail("provider_not_configured"));
        }

        return Task.FromResult(Result<ProviderConnect>.Ok(new ProviderConnect(
            "connected",
            state,
            null,
            "GitHub",
            null,
            installation,
            null,
            null,
            null,
            null,
            "GitHub App installation stored. The private key stays in the environment.")));
    }

    public Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Example)
        {
            return Task.FromResult(Result<ProviderCollect>.Ok(new ProviderCollect(ExampleSnapshots.For(ProviderKey, connection.Fixture))));
        }

        return CollectLiveAsync(connection, cancellationToken);
    }

    private async Task<Result<ProviderCollect>> CollectLiveAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.InstallationId))
        {
            return Result<ProviderCollect>.Fail("not_connected");
        }

        var token = await InstallationTokenAsync(connection.InstallationId, cancellationToken);
        if (token is null)
        {
            return Result<ProviderCollect>.Fail("github_auth_failed");
        }

        var api = configuration["Integrations:GitHubApiEndpoint"];
        if (string.IsNullOrWhiteSpace(api))
        {
            return Result<ProviderCollect>.Fail("provider_not_configured");
        }

        using var repos = await GetAsync(api.TrimEnd('/') + "/installation/repositories?per_page=100", token, cancellationToken);
        if (repos is null)
        {
            return Result<ProviderCollect>.Fail("github_collect_failed");
        }

        var resources = new List<NormalizedResource>();
        var orgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (repos.RootElement.TryGetProperty("repositories", out var rows))
        {
            foreach (var repo in rows.EnumerateArray())
            {
                var name = Text(repo, "full_name");
                var branch = Text(repo, "default_branch");
                var protectedBranch = await BranchProtectedAsync(api, token, name, branch, cancellationToken);
                resources.Add(new NormalizedResource("github", "repository", name, name, new Dictionary<string, string>
                {
                    ["protected"] = protectedBranch ? "true" : "false",
                }));
                if (repo.TryGetProperty("owner", out var owner) && Text(owner, "type") == "Organization")
                {
                    orgs.Add(Text(owner, "login"));
                }
            }
        }

        foreach (var org in orgs)
        {
            await AddMembersAsync(api, token, org, resources, cancellationToken);
        }

        return Result<ProviderCollect>.Ok(new ProviderCollect(resources));
    }

    private ProviderConnect Describe(string state, string? installation)
    {
        var url = IntegrationPolicies.AuthorizationUrl(configuration, ProviderKey, "install", state);
        var hint = url is null
            ? "Set GITHUB_APP_ID, GITHUB_APP_SLUG, and GITHUB_APP_PRIVATE_KEY. Permissions: repository administration read, metadata read, organization members read."
            : "Install the GitHub App, then send the installation id. The private key is not stored per tenant.";
        var status = string.IsNullOrWhiteSpace(installation) ? "pending" : "connected";
        return new ProviderConnect(status, state, null, null, null, installation, null, url, null, null, hint);
    }

    private async Task AddMembersAsync(string api, string token, string org, List<NormalizedResource> resources, CancellationToken cancellationToken)
    {
        using var members = await GetAsync(api.TrimEnd('/') + "/orgs/" + Uri.EscapeDataString(org) + "/members?per_page=100", token, cancellationToken);
        using var disabled = await GetAsync(api.TrimEnd('/') + "/orgs/" + Uri.EscapeDataString(org) + "/members?filter=2fa_disabled&per_page=100", token, cancellationToken);
        if (members is null)
        {
            return;
        }

        var withoutMfa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (disabled is not null)
        {
            foreach (var row in disabled.RootElement.EnumerateArray())
            {
                withoutMfa.Add(Text(row, "login"));
            }
        }

        foreach (var row in members.RootElement.EnumerateArray())
        {
            var login = Text(row, "login");
            var email = await PublicEmailAsync(api, token, login, cancellationToken);
            var mfa = !withoutMfa.Contains(login);
            resources.Add(new NormalizedResource("github", "user", login, login, new Dictionary<string, string>
            {
                ["email"] = email,
                ["mfa"] = mfa ? "true" : "false",
                ["offboarded"] = "false",
                ["status"] = "active",
            }));
        }
    }

    private async Task<bool> BranchProtectedAsync(string api, string token, string fullName, string branch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(branch))
        {
            return false;
        }

        var url = api.TrimEnd('/') + "/repos/" + fullName + "/branches/" + Uri.EscapeDataString(branch) + "/protection";
        using var request = Authorized(url, token);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> PublicEmailAsync(string api, string token, string login, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(api.TrimEnd('/') + "/users/" + Uri.EscapeDataString(login), token, cancellationToken);
        return document is null ? "" : Text(document.RootElement, "email");
    }

    private async Task<string?> InstallationTokenAsync(string installationId, CancellationToken cancellationToken)
    {
        var jwt = AppJwt();
        var api = configuration["Integrations:GitHubApiEndpoint"];
        if (jwt is null || string.IsNullOrWhiteSpace(api))
        {
            return null;
        }

        var url = api.TrimEnd('/') + "/app/installations/" + installationId + "/access_tokens";
        using var request = Authorized(url, jwt);
        request.Method = HttpMethod.Post;
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub installation token request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var token = Text(document.RootElement, "token");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private string? AppJwt()
    {
        var appId = configuration["GITHUB_APP_ID"];
        var pem = configuration["GITHUB_APP_PRIVATE_KEY"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        var normalized = pem.Replace("\\n", "\n", StringComparison.Ordinal);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(normalized);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = appId,
            Expires = DateTime.UtcNow.AddMinutes(8),
            IssuedAt = DateTime.UtcNow.AddSeconds(-30),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<JsonDocument?> GetAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = Authorized(url, token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
    }

    private static HttpRequestMessage Authorized(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("d2d-audit");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
