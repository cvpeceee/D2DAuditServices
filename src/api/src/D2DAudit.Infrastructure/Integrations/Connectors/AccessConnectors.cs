using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>
/// 1Password SCIM bridge: provisioned members. SCIM does not expose two-factor state, so members report
/// <c>mfa=unknown</c> unless the account enforces sign-in through the identity provider (see README).
/// </summary>
public sealed class OnePasswordConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "onepassword";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("scimBridgeUrl");
        var auth = Bearer(call.Require("bearerToken"));
        var resources = new List<NormalizedResource>();
        var start = 1;
        for (var count = 0; count < MaxPages; count++)
        {
            var page = await call.GetAsync<ScimPage>($"{origin}/scim/Users?startIndex={start}&count=100", auth, cancellationToken);
            resources.AddRange(page.Resources.Select(user =>
            {
                var email = user.Emails?.FirstOrDefault()?.Value ?? user.UserName;
                return call.Resource(
                    "vaultMember",
                    email,
                    user.DisplayName ?? Join(user.Name?.GivenName, user.Name?.FamilyName),
                    ("email", email),
                    ("mfa", "unknown"),
                    ("status", user.Active ? "active" : "suspended"),
                    ("role", "member"));
            }));
            start += page.Resources.Length;
            if (page.Resources.Length == 0 || start > page.TotalResults)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Bitwarden Public API: organization members with two-factor status (cloud or self-hosted).</summary>
public sealed class BitwardenConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Identity = "https://identity.bitwarden.com";
    private const string Api = "https://api.bitwarden.com";

    public override string Key => "bitwarden";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var selfHosted = call.Optional("baseUrl") is null ? null : call.BaseUrl("baseUrl");
        var identity = selfHosted is null ? Identity : selfHosted + "/identity";
        var api = selfHosted is null ? Api : selfHosted + "/api";
        var token = await call.PostFormAsync<OAuthToken>(
            identity + "/connect/token",
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "api.organization",
                ["client_id"] = call.Require("clientId"),
                ["client_secret"] = call.Require("clientSecret"),
            },
            static _ => { },
            cancellationToken);
        var members = await call.GetAsync<BitwardenMembers>(api + "/public/members", Bearer(token.AccessToken), cancellationToken);
        return members.Data
            .Select(member => call.Resource(
                "vaultMember",
                member.Email,
                member.Name ?? member.Email,
                ("email", member.Email),
                ("mfa", member.TwoFactorEnabled),
                ("status", member.Status < 0 ? "suspended" : "active"),
                ("role", member.Type switch { 0 => "owner", 1 => "admin", 3 => "manager", _ => "member" })))
            .ToArray();
    }
}

/// <summary>LastPass Enterprise API: getuserdata (multifactor and disabled flags).</summary>
public sealed class LastPassConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://lastpass.com/enterpriseapi.php";

    public override string Key => "lastpass";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var result = await call.PostJsonAsync<LastPassUsers>(
            Api,
            new { cid = call.Require("cid"), provhash = call.Require("provhash"), cmd = "getuserdata", data = new { } },
            static _ => { },
            cancellationToken);
        return (result.Users ?? [])
            .Select(pair => pair.Value)
            .Where(user => !string.IsNullOrWhiteSpace(user.Username))
            .Select(user => call.Resource(
                "vaultMember",
                user.Username!,
                user.Fullname ?? user.Username!,
                ("email", user.Username),
                ("mfa", !string.IsNullOrWhiteSpace(user.Multifactor)),
                ("status", user.Disabled ? "suspended" : "active"),
                ("role", user.Admin ? "admin" : "member")))
            .ToArray();
    }
}

/// <summary>Curricula (Huntress SAT) API: learner assignments; base URL is the tenant API root.</summary>
public sealed class CurriculaConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "curricula";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl");
        var auth = Bearer(call.Require("apiKey"));
        var resources = new List<NormalizedResource>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await call.GetAsync<CurriculaPage>($"{origin}/api/v1/assignments?page={page}&per_page=100", auth, cancellationToken);
            resources.AddRange(result.Data.Where(item => item.Learner?.Email is not null).Select(item => Training.Assignment(
                call,
                item.Learner!.Email!,
                item.Learner.Name,
                item.Course?.Title,
                item.CompletedAt is not null || string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase),
                item.Learner.Active != false)));
            if (result.Meta is null || page >= result.Meta.LastPage)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Hoxhunt external GraphQL API: users with their training completion.</summary>
public sealed class HoxhuntConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Query = """
        query Users($first: Int, $skip: Int) {
          users(first: $first, skip: $skip) {
            emails { address }
            profile { firstName lastName }
            isActive
            training { completedAt courseName }
          }
        }
        """;

    public override string Key => "hoxhunt";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var endpoint = call.BaseUrl("baseUrl", "api.hoxhunt.com") + "/graphql-external";
        var auth = Scheme("Authtoken", call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var result = await call.PostJsonAsync<HoxhuntResponse>(endpoint, new { query = Query, variables = new { first = 500, skip = page * 500 } }, auth, cancellationToken);
            var users = result.Data?.Users ?? throw new ConnectorException("invalid_response");
            resources.AddRange(users.Where(user => user.Emails?.FirstOrDefault()?.Address is not null).Select(user => Training.Assignment(
                call,
                user.Emails![0].Address!,
                Join(user.Profile?.FirstName, user.Profile?.LastName),
                user.Training?.CourseName ?? "Hoxhunt training",
                user.Training?.CompletedAt is not null,
                user.IsActive != false)));
            if (users.Length < 500)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Certn HR API: screening applications with report result and package.</summary>
public sealed class CertnConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private static readonly string[] Cleared = ["CLEARED", "PASS", "CLEAR", "COMPLETE_CLEAR"];

    public override string Key => "certn";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl", "api.certn.co");
        var auth = Scheme("Api-Key", call.Require("apiKey"));
        var resources = new List<NormalizedResource>();
        string? next = $"{origin}/hr/v1/applications/?page_size=100";
        for (var count = 0; count < MaxPages && next is not null; count++)
        {
            var page = await call.GetAsync<CertnPage>(next, auth, cancellationToken);
            resources.AddRange(page.Results.Select(application => call.Resource(
                "backgroundCheck",
                "app-" + application.Id,
                Join(application.Applicant?.FirstName, application.Applicant?.LastName),
                ("email", application.Applicant?.Email),
                ("cleared", Cleared.Contains(application.Result?.ToUpperInvariant())),
                ("status", application.ReportStatus?.ToLowerInvariant()),
                ("package", application.Package))));
            next = Within(page.Next, origin);
        }

        return resources;
    }
}

internal static class Training
{
    public static NormalizedResource Assignment(ConnectorCall call, string email, string? name, string? course, bool completed, bool active) =>
        call.Resource(
            "training",
            email,
            string.IsNullOrWhiteSpace(name) ? email : name,
            ("email", email),
            ("course", course),
            ("completed", completed),
            ("state", completed ? "Complete" : "Assigned"),
            ("status", active ? "active" : "terminated"));
}

internal sealed record ScimPage(ScimUser[] Resources, int TotalResults);

internal sealed record ScimUser(string UserName, string? DisplayName, bool Active, ScimName? Name, ScimEmail[]? Emails);

internal sealed record ScimName(string? GivenName, string? FamilyName);

internal sealed record ScimEmail(string? Value);

internal sealed record BitwardenMembers(BitwardenMember[] Data);

internal sealed record BitwardenMember(string Email, string? Name, bool TwoFactorEnabled, int Status, int Type);

internal sealed record LastPassUsers(Dictionary<string, LastPassUser>? Users);

internal sealed record LastPassUser(string? Username, string? Fullname, string? Multifactor, bool Disabled, bool Admin);

internal sealed record CurriculaPage(CurriculaAssignment[] Data, CurriculaMeta? Meta);

internal sealed record CurriculaMeta([property: JsonPropertyName("last_page")] int LastPage);

internal sealed record CurriculaAssignment(
    string? Status,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    CurriculaLearner? Learner,
    CurriculaCourse? Course);

internal sealed record CurriculaLearner(string? Email, string? Name, bool? Active);

internal sealed record CurriculaCourse(string? Title);

internal sealed record HoxhuntResponse(HoxhuntData? Data);

internal sealed record HoxhuntData(HoxhuntUser[]? Users);

internal sealed record HoxhuntUser(HoxhuntEmail[]? Emails, HoxhuntProfile? Profile, bool? IsActive, HoxhuntTraining? Training);

internal sealed record HoxhuntEmail(string? Address);

internal sealed record HoxhuntProfile(string? FirstName, string? LastName);

internal sealed record HoxhuntTraining(DateTimeOffset? CompletedAt, string? CourseName);

internal sealed record CertnPage(CertnApplication[] Results, string? Next);

internal sealed record CertnApplication(
    string Id,
    CertnApplicant? Applicant,
    [property: JsonPropertyName("report_status")] string? ReportStatus,
    string? Result,
    string? Package);

internal sealed record CertnApplicant(
    string? Email,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName);
