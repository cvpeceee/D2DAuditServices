using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Asana API 1.0: tasks in the change project with approval status and a Risk custom field.</summary>
public sealed class AsanaConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://app.asana.com/api/1.0";
    private const string Fields = "name,completed,approval_status,assignee.email,custom_fields.name,custom_fields.display_value";

    public override string Key => "asana";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("accessToken"));
        var resources = new List<NormalizedResource>();
        string? next = $"{Api}/projects/{Escape(call.Require("projectId"))}/tasks?limit=100&opt_fields={Fields}";
        for (var count = 0; count < MaxPages && next is not null; count++)
        {
            var page = await call.GetAsync<AsanaPage>(next, auth, cancellationToken);
            resources.AddRange(page.Data.Select(task =>
            {
                var approval = Field(task, "approv");
                return call.Resource(
                    "change",
                    task.Gid,
                    task.Name,
                    ("approved", task.ApprovalStatus == "approved" || Changes.IsYes(approval)),
                    ("approver", task.Assignee?.Email),
                    ("risk", Changes.Risk(Field(task, "risk"))),
                    ("state", task.Completed ? "closed" : "open"));
            }));
            next = Within(page.NextPage?.Uri, Api);
        }

        return resources;
    }

    private static string? Field(AsanaTask task, string marker) =>
        task.CustomFields?.FirstOrDefault(field => field.Name?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true)?.DisplayValue;
}

/// <summary>ServiceNow Table API: change_request records updated in the last 90 days.</summary>
public sealed class ServiceNowConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Fields = "sys_id,number,short_description,approval,risk,state,assigned_to";

    public override string Key => "servicenow";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("instanceUrl");
        var auth = Basic(call.Require("username"), call.Require("password"));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var result = await call.GetAsync<ServiceNowResult>(
                $"{origin}/api/now/table/change_request?sysparm_limit=100&sysparm_offset={page * 100}&sysparm_display_value=true&sysparm_exclude_reference_link=true&sysparm_fields={Fields}&sysparm_query={Escape("sys_updated_on>=javascript:gs.daysAgoStart(90)^ORDERBYDESCsys_updated_on")}",
                auth,
                cancellationToken);
            resources.AddRange(result.Result.Select(change => call.Resource(
                "change",
                change.Number,
                Join(change.Number, change.ShortDescription),
                ("approved", string.Equals(change.Approval, "Approved", StringComparison.OrdinalIgnoreCase)),
                ("approver", change.AssignedTo),
                ("risk", Changes.Risk(change.Risk)),
                ("state", change.State?.ToLowerInvariant()))));
            if (result.Result.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>ClickUp API v2: tasks (including closed) on the change list with approval and risk custom fields.</summary>
public sealed class ClickUpConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.clickup.com/api/v2";

    public override string Key => "clickup";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Headers(("Authorization", call.Require("apiToken")));
        var list = Escape(call.Require("listId"));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var result = await call.GetAsync<ClickUpPage>($"{Api}/list/{list}/task?page={page}&include_closed=true&subtasks=false", auth, cancellationToken);
            resources.AddRange(result.Tasks.Select(task => call.Resource(
                "change",
                task.CustomId ?? task.Id,
                task.Name,
                ("approved", Changes.IsYes(Field(task, "approv"))),
                ("approver", task.Assignees?.Select(assignee => assignee.Email).FirstOrDefault(email => email is not null)),
                ("risk", Changes.Risk(Field(task, "risk"))),
                ("state", task.Status?.Status))));
            if (result.LastPage != false)
            {
                break;
            }
        }

        return resources;
    }

    private static string? Field(ClickUpTask task, string marker) =>
        task.CustomFields?
            .Where(field => field.Name?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true)
            .Select(field => field.Value is { } value ? Read(value) : null)
            .FirstOrDefault();
}

/// <summary>Zoom API v2 (server-to-server OAuth): users plus account two-factor and SSO sign-in.</summary>
public sealed class ZoomConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Token = "https://zoom.us/oauth/token";
    private const string Api = "https://api.zoom.us/v2";
    private const int SsoLogin = 101;

    public override string Key => "zoom";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var token = await call.PostFormAsync<OAuthToken>(
            $"{Token}?grant_type=account_credentials&account_id={Escape(call.Require("accountId"))}",
            new Dictionary<string, string>(),
            Basic(call.Require("clientId"), call.Require("clientSecret")),
            cancellationToken);
        var auth = Bearer(token.AccessToken);
        var settings = await call.GetAsync<ZoomSecurity>($"{Api}/accounts/me/settings?option=security", auth, cancellationToken);
        var accountMfa = settings.SignInWithTwoFactorAuth;
        var resources = new List<NormalizedResource>();
        string? next = null;
        for (var count = 0; count < MaxPages; count++)
        {
            var url = $"{Api}/users?page_size=300" + (string.IsNullOrEmpty(next) ? string.Empty : "&next_page_token=" + Escape(next));
            var page = await call.GetAsync<ZoomUsers>(url, auth, cancellationToken);
            resources.AddRange(page.Users.Select(user => call.Resource(
                "user",
                user.Email,
                Join(user.FirstName, user.LastName),
                ("email", user.Email),
                ("mfa", accountMfa || user.LoginTypes?.Contains(SsoLogin) == true),
                ("status", user.Status == "inactive" ? "suspended" : "active"),
                ("offboarded", false),
                ("department", user.Dept),
                ("role", user.Type == 1 ? "Basic" : "Licensed"))));
            next = page.NextPageToken;
            if (string.IsNullOrEmpty(next))
            {
                break;
            }
        }

        return resources;
    }
}

internal static class Changes
{
    public static bool IsYes(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("approved", StringComparison.OrdinalIgnoreCase));

    public static string Risk(string? value)
    {
        var text = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Contains("high", StringComparison.Ordinal) || text is "1" or "2")
        {
            return "high";
        }

        return text.Contains("low", StringComparison.Ordinal) || text == "4" ? "low" : "medium";
    }
}

internal sealed record AsanaPage(AsanaTask[] Data, [property: JsonPropertyName("next_page")] AsanaNext? NextPage);

internal sealed record AsanaNext(string? Uri);

internal sealed record AsanaTask(
    string Gid,
    string Name,
    bool Completed,
    [property: JsonPropertyName("approval_status")] string? ApprovalStatus,
    AsanaUser? Assignee,
    [property: JsonPropertyName("custom_fields")] AsanaField[]? CustomFields);

internal sealed record AsanaUser(string? Email);

internal sealed record AsanaField(string? Name, [property: JsonPropertyName("display_value")] string? DisplayValue);

internal sealed record ServiceNowResult(ServiceNowChange[] Result);

internal sealed record ServiceNowChange(
    string Number,
    [property: JsonPropertyName("short_description")] string? ShortDescription,
    string? Approval,
    string? Risk,
    string? State,
    [property: JsonPropertyName("assigned_to")] string? AssignedTo);

internal sealed record ClickUpPage(ClickUpTask[] Tasks, [property: JsonPropertyName("last_page")] bool? LastPage);

internal sealed record ClickUpTask(
    string Id,
    [property: JsonPropertyName("custom_id")] string? CustomId,
    string Name,
    ClickUpStatus? Status,
    ClickUpAssignee[]? Assignees,
    [property: JsonPropertyName("custom_fields")] ClickUpField[]? CustomFields);

internal sealed record ClickUpStatus(string? Status);

internal sealed record ClickUpAssignee(string? Email);

internal sealed record ClickUpField(string? Name, JsonElement? Value);

/// <summary>Accepts both the flat and the nested `security` shapes of the account settings response.</summary>
internal sealed record ZoomSecurity(
    ZoomSecuritySettings? Security,
    [property: JsonPropertyName("sign_in_with_two_factor_auth")] bool? TwoFactor)
{
    public bool SignInWithTwoFactorAuth => TwoFactor == true || Security?.SignInWithTwoFactorAuth == true;
}

internal sealed record ZoomSecuritySettings([property: JsonPropertyName("sign_in_with_two_factor_auth")] bool? SignInWithTwoFactorAuth);

internal sealed record ZoomUsers(ZoomUser[] Users, [property: JsonPropertyName("next_page_token")] string? NextPageToken);

internal sealed record ZoomUser(
    string Email,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    string? Status,
    string? Dept,
    int Type,
    [property: JsonPropertyName("login_types")] int[]? LoginTypes);
