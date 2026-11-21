using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>BambooHR custom report: POST /v1/reports/custom including terminated employees.</summary>
public sealed class BambooHrConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.bamboohr.com/api/gateway.php/";

    public override string Key => "bamboohr";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var url = Api + Escape(call.Require("subdomain")) + "/v1/reports/custom?format=JSON&onlyCurrent=false";
        var report = await call.PostJsonAsync<BambooReport>(
            url,
            new { title = "D2D Audit people", fields = new[] { "firstName", "lastName", "workEmail", "department", "jobTitle", "status" } },
            Basic(call.Require("apiKey"), "x"),
            cancellationToken);
        return report.Employees
            .Select(item => call.Resource(
                "employee",
                "emp-" + item.Id,
                Join(item.FirstName, item.LastName),
                ("email", item.WorkEmail),
                ("status", string.Equals(item.Status, "Inactive", StringComparison.OrdinalIgnoreCase) ? "terminated" : "active"),
                ("department", item.Department),
                ("title", item.JobTitle)))
            .ToArray();
    }
}

/// <summary>Rippling Platform API: /platform/api/employees/include_terminated with offset paging.</summary>
public sealed class RipplingConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "rippling";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var people = await call.GetAsync<RipplingEmployee[]>(
                $"https://api.rippling.com/platform/api/employees/include_terminated?limit=100&offset={page * 100}",
                auth,
                cancellationToken);
            resources.AddRange(people.Select(item => call.Resource(
                "employee",
                "emp-" + item.Id,
                item.Name ?? item.WorkEmail ?? item.Id,
                ("email", item.WorkEmail),
                ("status", item.RoleState == "TERMINATED" ? "terminated" : "active"),
                ("department", Read(item.Department, "name")),
                ("title", item.Title))));
            if (people.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Gusto API: /v1/companies/{id}/employees with page paging.</summary>
public sealed class GustoConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "gusto";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var token = call.Require("accessToken");
        var auth = (Action<HttpRequestMessage>)(request =>
        {
            Bearer(token)(request);
            request.Headers.TryAddWithoutValidation("X-Gusto-API-Version", "2024-04-01");
        });
        var company = Escape(call.Require("companyId"));
        var resources = new List<NormalizedResource>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var people = await call.GetAsync<GustoEmployee[]>(
                $"https://api.gusto.com/v1/companies/{company}/employees?page={page}&per=100",
                auth,
                cancellationToken);
            resources.AddRange(people.Select(item => call.Resource(
                "employee",
                "emp-" + item.Uuid,
                Join(item.FirstName, item.LastName),
                ("email", item.WorkEmail ?? item.Email),
                ("status", item.Terminated ? "terminated" : "active"),
                ("department", item.Department),
                ("title", item.Jobs?.FirstOrDefault()?.Title))));
            if (people.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Workday RaaS: a custom report exposed as JSON with Employee_ID, Worker, Email, Active, Department and Business_Title columns.</summary>
public sealed class WorkdayConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "workday";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var url = Https(call.Require("reportUrl"));
        if (!url.Contains("format=json", StringComparison.OrdinalIgnoreCase))
        {
            url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "format=json";
        }

        var report = await call.GetAsync<WorkdayReport>(url, Basic(call.Require("username"), call.Require("password")), cancellationToken);
        return report.Entries
            .Select(item => call.Resource(
                "employee",
                "emp-" + item.EmployeeId,
                item.Worker ?? item.EmployeeId,
                ("email", item.Email),
                ("status", Read(item.Active) is "0" or "false" ? "terminated" : "active"),
                ("department", item.Department),
                ("title", item.Title)))
            .ToArray();
    }
}

/// <summary>HiBob API: POST /v1/people/search with showInactive.</summary>
public sealed class HiBobConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Search = "https://api.hibob.com/v1/people/search";

    public override string Key => "hibob";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var result = await call.PostJsonAsync<HiBobSearch>(
            Search,
            new
            {
                showInactive = true,
                fields = new[] { "root.id", "root.email", "root.displayName", "work.department", "work.title", "internal.lifecycleStatus" },
            },
            Basic(call.Require("serviceUserId"), call.Require("serviceUserToken")),
            cancellationToken);
        return result.Employees
            .Select(item => call.Resource(
                "employee",
                "emp-" + item.Id,
                item.DisplayName ?? item.Email ?? item.Id,
                ("email", item.Email),
                ("status", string.Equals(item.Internal?.LifecycleStatus, "terminated", StringComparison.OrdinalIgnoreCase) ? "terminated" : "active"),
                ("department", item.Work?.Department),
                ("title", item.Work?.Title)))
            .ToArray();
    }
}

/// <summary>Deel REST v2: /rest/v2/people with offset paging.</summary>
public sealed class DeelConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "deel";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var people = await call.GetAsync<DeelPage>($"https://api.letsdeel.com/rest/v2/people?limit=100&offset={page * 100}", auth, cancellationToken);
            resources.AddRange(people.Data.Select(item => call.Resource(
                "employee",
                "emp-" + item.Id,
                item.FullName ?? item.WorkEmail ?? item.Id,
                ("email", item.WorkEmail),
                ("status", string.Equals(item.HiringStatus, "terminated", StringComparison.OrdinalIgnoreCase) ? "terminated" : "active"),
                ("department", Read(item.Department, "name")),
                ("title", item.JobTitle))));
            if (people.Data.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

internal sealed record BambooReport(BambooEmployee[] Employees);

internal sealed record BambooEmployee(string Id, string? FirstName, string? LastName, string? WorkEmail, string? Department, string? JobTitle, string? Status);

internal sealed record RipplingEmployee(string Id, string? Name, string? WorkEmail, string? RoleState, JsonElement Department, string? Title);

internal sealed record GustoEmployee(
    string Uuid,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    string? Email,
    [property: JsonPropertyName("work_email")] string? WorkEmail,
    bool Terminated,
    string? Department,
    GustoJob[]? Jobs);

internal sealed record GustoJob(string? Title);

internal sealed record WorkdayReport([property: JsonPropertyName("Report_Entry")] WorkdayEntry[] Entries);

internal sealed record WorkdayEntry(
    [property: JsonPropertyName("Employee_ID")] string EmployeeId,
    [property: JsonPropertyName("Worker")] string? Worker,
    [property: JsonPropertyName("Email")] string? Email,
    [property: JsonPropertyName("Active")] JsonElement Active,
    [property: JsonPropertyName("Department")] string? Department,
    [property: JsonPropertyName("Business_Title")] string? Title);

internal sealed record HiBobSearch(HiBobPerson[] Employees);

internal sealed record HiBobPerson(string Id, string? Email, string? DisplayName, HiBobWork? Work, HiBobInternal? Internal);

internal sealed record HiBobWork(string? Department, string? Title);

internal sealed record HiBobInternal(string? LifecycleStatus);

internal sealed record DeelPage(DeelPerson[] Data);

internal sealed record DeelPerson(
    string Id,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("work_email")] string? WorkEmail,
    [property: JsonPropertyName("hiring_status")] string? HiringStatus,
    JsonElement Department,
    [property: JsonPropertyName("job_title")] string? JobTitle);
