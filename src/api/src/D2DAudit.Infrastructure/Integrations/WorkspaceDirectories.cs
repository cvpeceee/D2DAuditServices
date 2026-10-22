using System.Net.Http.Headers;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class GoogleWorkspaceDirectory(HttpClient http, IConfiguration configuration, ILogger<GoogleWorkspaceDirectory> logger) : IWorkspaceDirectory
{
    public string ProviderKey => "google";

    public async Task<Result<ProviderConnect>> ConnectAsync(
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (existing?.Example == true)
        {
            return Result<ProviderConnect>.Ok(Pending(redirectUri, existing.ExternalId ?? Guid.NewGuid().ToString("N")));
        }

        var state = request.State ?? "";
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result<ProviderConnect>.Ok(Pending(redirectUri, state));
        }

        var refresh = await ExchangeAsync(request.Code, redirectUri, cancellationToken);
        if (refresh is null)
        {
            return Result<ProviderConnect>.Fail("oauth_exchange_failed");
        }

        return Result<ProviderConnect>.Ok(new ProviderConnect(
            "connected",
            state,
            null,
            "Google Workspace",
            null,
            null,
            refresh,
            null,
            null,
            null,
            "Directory read is connected. The refresh token is encrypted before it is stored."));
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
        if (string.IsNullOrWhiteSpace(connection.Secret))
        {
            return Result<ProviderCollect>.Fail("not_connected");
        }

        var access = await RefreshAsync(connection.Secret, cancellationToken);
        if (access is null)
        {
            return Result<ProviderCollect>.Fail("oauth_refresh_failed");
        }

        var endpoint = configuration["Integrations:GoogleDirectoryEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Result<ProviderCollect>.Fail("provider_not_configured");
        }

        var users = new List<NormalizedResource>();
        string? page = null;
        for (var i = 0; i < 10; i++)
        {
            var url = endpoint.TrimEnd('/') + "/users?customer=my_customer&maxResults=200";
            if (!string.IsNullOrWhiteSpace(page))
            {
                url += "&pageToken=" + Uri.EscapeDataString(page);
            }

            using var document = await GetAsync(url, access, cancellationToken);
            if (document is null)
            {
                return Result<ProviderCollect>.Fail("directory_collect_failed");
            }

            if (document.RootElement.TryGetProperty("users", out var rows))
            {
                foreach (var user in rows.EnumerateArray())
                {
                    var suspended = Bool(user, "suspended") || Bool(user, "archived");
                    var email = Text(user, "primaryEmail");
                    var name = user.TryGetProperty("name", out var nameNode) ? Text(nameNode, "fullName") : email;
                    users.Add(User(Text(user, "id"), name, email, Bool(user, "isEnrolledIn2Sv"), suspended));
                }
            }

            page = Text(document.RootElement, "nextPageToken");
            if (string.IsNullOrWhiteSpace(page))
            {
                break;
            }
        }

        return Result<ProviderCollect>.Ok(new ProviderCollect(users));
    }

    private ProviderConnect Pending(string redirectUri, string state)
    {
        var url = IntegrationPolicies.AuthorizationUrl(configuration, ProviderKey, redirectUri, state);
        var hint = url is null
            ? "Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET. Scope is directory user read-only."
            : "An admin grants directory user read-only access. The refresh token is encrypted with TENANT_DATA_KEY.";
        return new ProviderConnect("pending", state, null, null, null, null, null, url, null, null, hint);
    }

    private async Task<string?> ExchangeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = configuration["GOOGLE_CLIENT_ID"] ?? "",
            ["client_secret"] = configuration["GOOGLE_CLIENT_SECRET"] ?? "",
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        };
        return await TokenAsync(payload, "refresh_token", cancellationToken);
    }

    private Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = configuration["GOOGLE_CLIENT_ID"] ?? "",
            ["client_secret"] = configuration["GOOGLE_CLIENT_SECRET"] ?? "",
            ["grant_type"] = "refresh_token",
        };
        return TokenAsync(payload, "access_token", cancellationToken);
    }

    private async Task<string?> TokenAsync(Dictionary<string, string> payload, string field, CancellationToken cancellationToken)
    {
        var endpoint = configuration["Integrations:GoogleTokenEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(payload["client_id"]) || string.IsNullOrWhiteSpace(payload["client_secret"]))
        {
            return null;
        }

        using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(payload), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google token request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return Text(document.RootElement, field);
    }

    private async Task<JsonDocument?> GetAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google directory request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static NormalizedResource User(string id, string name, string email, bool mfa, bool suspended) =>
        new("google", "user", id, string.IsNullOrWhiteSpace(name) ? email : name, new Dictionary<string, string>
        {
            ["email"] = email,
            ["mfa"] = mfa ? "true" : "false",
            ["offboarded"] = suspended ? "true" : "false",
            ["status"] = suspended ? "suspended" : "active",
        });

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class Microsoft365Directory(HttpClient http, IConfiguration configuration, ILogger<Microsoft365Directory> logger) : IWorkspaceDirectory
{
    public string ProviderKey => "microsoft";

    public Task<Result<ProviderConnect>> ConnectAsync(
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (existing?.Example == true)
        {
            return Task.FromResult(Result<ProviderConnect>.Ok(Pending(redirectUri, existing.ExternalId ?? Guid.NewGuid().ToString("N"), existing.Domain)));
        }

        var state = request.State ?? "";
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Task.FromResult(Result<ProviderConnect>.Ok(Pending(redirectUri, state, null)));
        }

        if (!Guid.TryParse(request.Code, out var tenant))
        {
            return Task.FromResult(Result<ProviderConnect>.Fail("invalid_tenant"));
        }

        if (string.IsNullOrWhiteSpace(configuration["MS_CLIENT_ID"]) || string.IsNullOrWhiteSpace(configuration["MS_CLIENT_SECRET"]))
        {
            return Task.FromResult(Result<ProviderConnect>.Fail("provider_not_configured"));
        }

        return Task.FromResult(Result<ProviderConnect>.Ok(new ProviderConnect(
            "connected",
            state,
            null,
            "Microsoft 365",
            tenant.ToString(),
            null,
            null,
            null,
            null,
            null,
            "Admin consent is recorded. Collection uses application permissions. No refresh token is stored.")));
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
        if (string.IsNullOrWhiteSpace(connection.Domain))
        {
            return Result<ProviderCollect>.Fail("not_connected");
        }

        var access = await TokenAsync(connection.Domain, cancellationToken);
        if (access is null)
        {
            return Result<ProviderCollect>.Fail("oauth_refresh_failed");
        }

        var graph = configuration["Integrations:MicrosoftGraphEndpoint"];
        if (string.IsNullOrWhiteSpace(graph))
        {
            return Result<ProviderCollect>.Fail("provider_not_configured");
        }

        using var usersDocument = await GetAsync(graph.TrimEnd('/') + "/users?$select=id,displayName,mail,userPrincipalName,accountEnabled&$top=200", access, cancellationToken);
        using var mfaDocument = await GetAsync(graph.TrimEnd('/') + "/reports/authenticationMethods/userRegistrationDetails?$top=200", access, cancellationToken);
        if (usersDocument is null || mfaDocument is null)
        {
            return Result<ProviderCollect>.Fail("directory_collect_failed");
        }

        var mfa = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (mfaDocument.RootElement.TryGetProperty("value", out var mfaRows))
        {
            foreach (var row in mfaRows.EnumerateArray())
            {
                var upn = Text(row, "userPrincipalName");
                if (upn.Length > 0)
                {
                    mfa[upn] = Bool(row, "isMfaRegistered");
                }
            }
        }

        var users = new List<NormalizedResource>();
        if (usersDocument.RootElement.TryGetProperty("value", out var rows))
        {
            foreach (var user in rows.EnumerateArray())
            {
                var upn = Text(user, "userPrincipalName");
                var email = Text(user, "mail");
                if (string.IsNullOrWhiteSpace(email))
                {
                    email = upn;
                }

                var enabled = !user.TryGetProperty("accountEnabled", out var flag) || flag.ValueKind != JsonValueKind.False;
                var enrolled = mfa.TryGetValue(upn, out var registered) && registered;
                users.Add(new NormalizedResource("microsoft", "user", Text(user, "id"), Text(user, "displayName"), new Dictionary<string, string>
                {
                    ["email"] = email,
                    ["mfa"] = enrolled ? "true" : "false",
                    ["offboarded"] = enabled ? "false" : "true",
                    ["status"] = enabled ? "active" : "suspended",
                }));
            }
        }

        return Result<ProviderCollect>.Ok(new ProviderCollect(users));
    }

    private ProviderConnect Pending(string redirectUri, string state, string? domain)
    {
        var url = IntegrationPolicies.AuthorizationUrl(configuration, ProviderKey, redirectUri, state);
        var hint = url is null
            ? "Set MS_CLIENT_ID and MS_CLIENT_SECRET. Admin consent uses application permissions."
            : "Send the directory tenant id from the admin-consent redirect back as the code. No refresh token is stored.";
        return new ProviderConnect("pending", state, null, null, domain, null, null, url, null, null, hint);
    }

    private async Task<string?> TokenAsync(string tenant, CancellationToken cancellationToken)
    {
        var endpoint = configuration["Integrations:MicrosoftTokenEndpoint"];
        var clientId = configuration["MS_CLIENT_ID"];
        var secret = configuration["MS_CLIENT_SECRET"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var tokenEndpoint = endpoint.Replace("/organizations/", "/" + tenant + "/", StringComparison.Ordinal);
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = configuration["Integrations:MicrosoftGraphScope"] ?? "",
            ["grant_type"] = "client_credentials",
        };
        using var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(payload), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Microsoft token request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return Text(document.RootElement, "access_token");
    }

    private async Task<JsonDocument?> GetAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Microsoft Graph request failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
