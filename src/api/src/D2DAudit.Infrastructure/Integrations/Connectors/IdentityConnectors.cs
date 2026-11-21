using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Okta Users API: GET /api/v1/users with Link paging and /factors per user.</summary>
public sealed class OktaConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "okta";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("domain");
        var auth = Scheme("SSWS", call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        var next = origin + "/api/v1/users?limit=200";
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            var (users, headers) = await call.GetPageAsync<OktaUser[]>(next, auth, cancellationToken);
            foreach (var user in users)
            {
                var factors = await call.GetAsync<OktaFactor[]>(origin + "/api/v1/users/" + Escape(user.Id) + "/factors", auth, cancellationToken);
                var email = user.Profile.Email ?? user.Profile.Login ?? user.Id;
                resources.Add(call.Resource(
                    "user",
                    email,
                    Join(user.Profile.FirstName, user.Profile.LastName),
                    ("email", email),
                    ("mfa", factors.Any(factor => factor.Status == "ACTIVE")),
                    ("status", user.Status is "SUSPENDED" or "DEPROVISIONED" or "LOCKED_OUT" ? "suspended" : "active"),
                    ("offboarded", user.Status == "DEPROVISIONED"),
                    ("department", user.Profile.Department),
                    ("role", user.Profile.Title)));
            }

            next = Within(NextLink(headers), origin);
        }

        return resources;
    }
}

/// <summary>JumpCloud v1 API: /api/systemusers and /api/systems with skip paging.</summary>
public sealed class JumpCloudConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Origin = "https://console.jumpcloud.com";

    public override string Key => "jumpcloud";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var headers = new List<(string, string)> { ("x-api-key", call.Require("apiKey")) };
        if (call.Optional("orgId") is { } org)
        {
            headers.Add(("x-org-id", org));
        }

        var auth = Headers([.. headers]);
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var users = await call.GetAsync<JumpCloudList<JumpCloudUser>>($"{Origin}/api/systemusers?limit=100&skip={page * 100}", auth, cancellationToken);
            resources.AddRange(users.Results.Select(user => call.Resource(
                "user",
                user.Email,
                Join(user.Firstname, user.Lastname),
                ("email", user.Email),
                ("mfa", user.TotpEnabled || user.Mfa?.Configured == true),
                ("status", user.Suspended || user.State == "SUSPENDED" ? "suspended" : "active"),
                ("offboarded", user.Suspended),
                ("department", user.Department),
                ("role", user.JobTitle))));
            if (users.Results.Length < 100)
            {
                break;
            }
        }

        for (var page = 0; page < MaxPages; page++)
        {
            var systems = await call.GetAsync<JumpCloudList<JumpCloudSystem>>($"{Origin}/api/systems?limit=100&skip={page * 100}", auth, cancellationToken);
            resources.AddRange(systems.Results.Select(system => call.Resource(
                "device",
                system.SerialNumber ?? system.Id,
                system.Hostname ?? system.Id,
                ("platform", system.Os),
                ("encrypted", system.Fde?.Active == true))));
            if (systems.Results.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>OneLogin API v2: client-credentials token, /api/2/users and MFA devices per user.</summary>
public sealed class OneLoginConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "onelogin";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = Https(call.Require("subdomain") + ".onelogin.com");
        var token = await call.PostJsonAsync<OAuthToken>(
            origin + "/auth/oauth2/v2/token",
            new { grant_type = "client_credentials" },
            Basic(call.Require("clientId"), call.Require("clientSecret")),
            cancellationToken);
        var auth = Bearer(token.AccessToken);
        var resources = new List<NormalizedResource>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = origin + "/api/2/users?limit=100" + (cursor is null ? "" : "&after_cursor=" + Escape(cursor));
            var (users, headers) = await call.GetPageAsync<OneLoginUser[]>(url, auth, cancellationToken);
            foreach (var user in users)
            {
                var devices = await call.GetAsync<OneLoginDevice[]>($"{origin}/api/2/mfa/users/{user.Id}/devices", auth, cancellationToken);
                resources.Add(call.Resource(
                    "user",
                    user.Email ?? user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Join(user.Firstname, user.Lastname),
                    ("email", user.Email),
                    ("mfa", devices.Length > 0),
                    ("status", user.Status == 1 ? "active" : "suspended"),
                    ("offboarded", user.Status == 2),
                    ("department", user.Department),
                    ("role", user.Title)));
            }

            cursor = Header(headers, "After-Cursor");
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Auth0 Management API v2: client-credentials token and /api/v2/users paging.</summary>
public sealed class Auth0Connector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "auth0";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("domain");
        var token = await call.PostJsonAsync<OAuthToken>(
            origin + "/oauth/token",
            new
            {
                client_id = call.Require("clientId"),
                client_secret = call.Require("clientSecret"),
                audience = origin + "/api/v2/",
                grant_type = "client_credentials",
            },
            static _ => { },
            cancellationToken);
        var auth = Bearer(token.AccessToken);
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var users = await call.GetAsync<Auth0User[]>($"{origin}/api/v2/users?per_page=100&page={page}", auth, cancellationToken);
            resources.AddRange(users.Select(user => call.Resource(
                "user",
                user.Email ?? user.UserId,
                user.Name ?? user.Email ?? user.UserId,
                ("email", user.Email),
                ("mfa", user.Multifactor is { Length: > 0 }),
                ("status", user.Blocked ? "suspended" : "active"),
                ("offboarded", user.Blocked))));
            if (users.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

internal sealed record OAuthToken([property: JsonPropertyName("access_token")] string AccessToken);

internal sealed record OktaUser(string Id, string Status, OktaProfile Profile);

internal sealed record OktaProfile(string? Login, string? Email, string? FirstName, string? LastName, string? Department, string? Title);

internal sealed record OktaFactor(string Id, string Status);

internal sealed record JumpCloudList<T>(T[] Results);

internal sealed record JumpCloudUser(
    [property: JsonPropertyName("_id")] string Id,
    string Email,
    string? Firstname,
    string? Lastname,
    string? Department,
    string? JobTitle,
    bool Suspended,
    string? State,
    [property: JsonPropertyName("totp_enabled")] bool TotpEnabled,
    JumpCloudMfa? Mfa);

internal sealed record JumpCloudMfa(bool Configured);

internal sealed record JumpCloudSystem(
    [property: JsonPropertyName("_id")] string Id,
    string? Hostname,
    string? Os,
    string? SerialNumber,
    JumpCloudFde? Fde);

internal sealed record JumpCloudFde(bool Active);

internal sealed record OneLoginUser(long Id, string? Email, string? Firstname, string? Lastname, string? Department, string? Title, int Status);

internal sealed record OneLoginDevice([property: JsonPropertyName("device_id")] System.Text.Json.JsonElement DeviceId);

internal sealed record Auth0User(
    [property: JsonPropertyName("user_id")] string UserId,
    string? Email,
    string? Name,
    bool Blocked,
    string[]? Multifactor);
