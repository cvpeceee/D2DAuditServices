using System.Globalization;
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

    public async Task<IReadOnlyList<ExternalChange>> MergedPullRequestsAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Example || string.IsNullOrWhiteSpace(connection.InstallationId))
        {
            return [];
        }

        var token = await InstallationTokenAsync(connection.InstallationId, cancellationToken);
        var api = configuration["Integrations:GitHubApiEndpoint"];
        if (token is null || string.IsNullOrWhiteSpace(api))
        {
            return [];
        }

        var root = api.TrimEnd('/');
        using var repos = await GetAsync(root + "/installation/repositories?per_page=10", token, cancellationToken);
        if (repos is null || !repos.RootElement.TryGetProperty("repositories", out var rows))
        {
            return [];
        }

        var changes = new List<ExternalChange>();
        var seen = 0;
        foreach (var repo in rows.EnumerateArray())
        {
            if (seen == 10)
            {
                break;
            }

            seen++;
            var name = Text(repo, "full_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            using var pulls = await GetAsync(root + "/repos/" + RepoPath(name) + "/pulls?state=closed&per_page=10&sort=updated&direction=desc", token, cancellationToken);
            if (pulls is null)
            {
                continue;
            }

            foreach (var pull in pulls.RootElement.EnumerateArray())
            {
                var merged = Text(pull, "merged_at");
                if (string.IsNullOrWhiteSpace(merged) || !DateTimeOffset.TryParse(merged, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deployedAt))
                {
                    continue;
                }

                var number = pull.TryGetProperty("number", out var numberValue) && numberValue.TryGetInt32(out var parsed) ? parsed : 0;
                if (number == 0)
                {
                    continue;
                }

                var title = Text(pull, "title");
                var author = pull.TryGetProperty("user", out var user) ? Text(user, "login") : "";
                var sha = Text(pull, "merge_commit_sha");
                var approver = await ApproverAsync(root, token, name, number, author, cancellationToken);
                var label = string.IsNullOrWhiteSpace(title) ? "Pull request " + number.ToString(CultureInfo.InvariantCulture) : title;
                changes.Add(new ExternalChange(
                    "github",
                    name + "#" + number.ToString(CultureInfo.InvariantCulture),
                    label,
                    name,
                    author,
                    approver.Length > 0,
                    approver,
                    deployedAt,
                    sha));
            }
        }

        return changes;
    }

    public async Task<IReadOnlyList<ImportedFinding>?> DependabotAlertsAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Example || string.IsNullOrWhiteSpace(connection.InstallationId))
        {
            return null;
        }

        var token = await InstallationTokenAsync(connection.InstallationId, cancellationToken);
        var api = configuration["Integrations:GitHubApiEndpoint"];
        if (token is null || string.IsNullOrWhiteSpace(api))
        {
            return null;
        }

        var root = api.TrimEnd('/');
        using var repos = await GetAsync(root + "/installation/repositories?per_page=10", token, cancellationToken);
        if (repos is null || !repos.RootElement.TryGetProperty("repositories", out var rows))
        {
            return null;
        }

        var findings = new List<ImportedFinding>();
        foreach (var repo in rows.EnumerateArray().Take(10))
        {
            var name = Text(repo, "full_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            using var alerts = await GetAsync(root + "/repos/" + RepoPath(name) + "/dependabot/alerts?state=open&per_page=20", token, cancellationToken);
            if (alerts is null || alerts.RootElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var alert in alerts.RootElement.EnumerateArray())
            {
                if (!alert.TryGetProperty("security_advisory", out var advisory))
                {
                    continue;
                }

                var cve = Text(advisory, "cve_id");
                var title = Text(advisory, "summary");
                var severity = Severity(Text(advisory, "severity"));
                var asset = name;
                if (alert.TryGetProperty("dependency", out var dependency) && dependency.TryGetProperty("package", out var package))
                {
                    var packageName = Text(package, "name");
                    if (packageName.Length > 0)
                    {
                        asset = packageName;
                    }
                }

                var fix = alert.TryGetProperty("security_vulnerability", out var vulnerability)
                    && vulnerability.TryGetProperty("first_patched_version", out var patched)
                    && patched.ValueKind != JsonValueKind.Null;
                if (cve.Length == 0)
                {
                    cve = "GHSA-" + Text(alert, "number");
                }

                if (cve.Length == 0 || title.Length == 0)
                {
                    continue;
                }

                findings.Add(new ImportedFinding("GitHub Dependabot", cve, title, severity, asset, fix));
            }
        }

        return findings;
    }

    private static string Severity(string value) => value.ToLowerInvariant() switch
    {
        "critical" => "Critical",
        "high" => "High",
        "low" => "Low",
        _ => "Medium",
    };

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

        var resources = new List<NormalizedResource>();
        var orgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var repoUrl = api.TrimEnd('/') + "/installation/repositories?per_page=100";
        var anyRepoPage = false;
        for (var page = 0; page < 20 && !string.IsNullOrWhiteSpace(repoUrl); page++)
        {
            var fetched = await GetPagedAsync(repoUrl, token, cancellationToken);
            if (fetched is null)
            {
                if (!anyRepoPage)
                {
                    return Result<ProviderCollect>.Fail("github_collect_failed");
                }

                break;
            }

            anyRepoPage = true;
            using (fetched.Document)
            {
                if (fetched.Document.RootElement.TryGetProperty("repositories", out var rows))
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
            }

            repoUrl = fetched.NextUrl;
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
        var withoutMfa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledUrl = api.TrimEnd('/') + "/orgs/" + Uri.EscapeDataString(org) + "/members?filter=2fa_disabled&per_page=100";
        for (var page = 0; page < 20 && !string.IsNullOrWhiteSpace(disabledUrl); page++)
        {
            var fetched = await GetPagedAsync(disabledUrl, token, cancellationToken);
            if (fetched is null)
            {
                break;
            }

            using (fetched.Document)
            {
                foreach (var row in fetched.Document.RootElement.EnumerateArray())
                {
                    withoutMfa.Add(Text(row, "login"));
                }
            }

            disabledUrl = fetched.NextUrl;
        }

        var membersUrl = api.TrimEnd('/') + "/orgs/" + Uri.EscapeDataString(org) + "/members?per_page=100";
        for (var page = 0; page < 20 && !string.IsNullOrWhiteSpace(membersUrl); page++)
        {
            var fetched = await GetPagedAsync(membersUrl, token, cancellationToken);
            if (fetched is null)
            {
                break;
            }

            using (fetched.Document)
            {
                foreach (var row in fetched.Document.RootElement.EnumerateArray())
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

            membersUrl = fetched.NextUrl;
        }
    }

    private async Task<string> ApproverAsync(string api, string token, string fullName, int number, string author, CancellationToken cancellationToken)
    {
        using var reviews = await GetAsync(
            api + "/repos/" + RepoPath(fullName) + "/pulls/" + number.ToString(CultureInfo.InvariantCulture) + "/reviews",
            token,
            cancellationToken);
        if (reviews is null)
        {
            return "";
        }

        foreach (var review in reviews.RootElement.EnumerateArray())
        {
            var state = Text(review, "state");
            var login = review.TryGetProperty("user", out var user) ? Text(user, "login") : "";
            if (state == "APPROVED" && login.Length > 0 && !string.Equals(login, author, StringComparison.OrdinalIgnoreCase))
            {
                return login;
            }
        }

        return "";
    }

    private static string RepoPath(string fullName)
    {
        var parts = fullName.Split('/', 2);
        if (parts.Length != 2)
        {
            return Uri.EscapeDataString(fullName);
        }

        return Uri.EscapeDataString(parts[0]) + "/" + Uri.EscapeDataString(parts[1]);
    }

    private async Task<bool> BranchProtectedAsync(string api, string token, string fullName, string branch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(branch))
        {
            return false;
        }

        var url = api.TrimEnd('/') + "/repos/" + RepoPath(fullName) + "/branches/" + Uri.EscapeDataString(branch) + "/protection";
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
        var fetched = await GetPagedAsync(url, token, cancellationToken);
        return fetched?.Document;
    }

    private async Task<PagedJson?> GetPagedAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = Authorized(url, token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string? nextUrl = null;
        if (response.Headers.TryGetValues("Link", out var linkValues))
        {
            foreach (var header in linkValues)
            {
                foreach (var segment in header.Split(','))
                {
                    if (!segment.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var start = segment.IndexOf('<');
                    var end = segment.IndexOf('>');
                    if (start >= 0 && end > start + 1)
                    {
                        var candidate = segment[(start + 1)..end].Trim();
                        if (SafeUrl.IsSameOrigin(candidate, url))
                        {
                            nextUrl = candidate;
                        }
                    }
                }
            }
        }

        return new PagedJson(JsonDocument.Parse(body), nextUrl);
    }

    private sealed record PagedJson(JsonDocument Document, string? NextUrl);

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
