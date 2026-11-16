using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Operations;

public sealed class WorkforceImport(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    HttpClient http,
    IConfiguration configuration,
    ILogger<WorkforceImport> logger) : IWorkforceImport
{
    public Task<Result<ImportCount>> OktaAsync(CancellationToken cancellationToken) =>
        ImportAsync(
            "okta",
            DirectorySource.Okta,
            configuration["OKTA_ORG_URL"],
            configuration["OKTA_API_TOKEN"],
            "/api/v1/users?limit=200",
            "SSWS",
            null,
            cancellationToken);

    public Task<Result<ImportCount>> HrisAsync(CancellationToken cancellationToken) =>
        ImportAsync(
            "hris",
            DirectorySource.Hris,
            configuration["BAMBOO_BASE_URL"],
            configuration["BAMBOO_API_KEY"],
            "/v1/employees/directory",
            "Basic",
            configuration["BAMBOO_API_KEY"],
            cancellationToken);

    private async Task<Result<ImportCount>> ImportAsync(
        string action,
        DirectorySource source,
        string? baseUrl,
        string? token,
        string path,
        string scheme,
        string? basicKey,
        CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ImportCount>.Fail("forbidden");
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            return Result<ImportCount>.Ok(new ImportCount(0));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = scheme == "Basic"
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basicKey + ":x")))
            : new AuthenticationHeaderValue(scheme, token);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Directory import failed with status {StatusCode}.", (int)response.StatusCode);
            return Result<ImportCount>.Fail("import_failed");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var rows = People(document.RootElement);
        await using var db = Open();
        var imported = 0;
        foreach (var row in rows)
        {
            var email = row.Email.Trim();
            if (email.Length is 0 or > 320 || !email.Contains('@'))
            {
                continue;
            }

            var person = await db.People.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
            if (person is null)
            {
                person = new DirectoryPerson
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Training = TrainingState.NotAssigned,
                    BackgroundStatus = "NotStarted",
                };
                db.People.Add(person);
                imported++;
            }

            person.Name = Clip(row.Name, 200);
            person.Department = Clip(row.Department, 120);
            person.Role = Clip(row.Role, 120);
            person.Source = source;
            person.State = row.Active ? PersonState.Active : PersonState.Offboarded;
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "people." + action, "ok", imported.ToString(), null, cancellationToken);
        return Result<ImportCount>.Ok(new ImportCount(imported));
    }

    private static List<DirectoryImport> People(JsonElement root)
    {
        var array = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("employees", out var employees))
            {
                array = employees;
            }
            else if (root.TryGetProperty("value", out var value))
            {
                array = value;
            }
        }

        var rows = new List<DirectoryImport>();
        if (array.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in array.EnumerateArray())
        {
            var email = Text(item, "email", "workEmail", "login");
            if (email.Length == 0 && item.TryGetProperty("profile", out var profile))
            {
                email = Text(profile, "email");
            }

            var name = Text(item, "displayName", "fullName", "name");
            if (name.Length == 0 && item.TryGetProperty("profile", out var named))
            {
                name = (Text(named, "firstName") + " " + Text(named, "lastName")).Trim();
            }

            var active = true;
            if (item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            {
                active = !string.Equals(status.GetString(), "DEPROVISIONED", StringComparison.OrdinalIgnoreCase);
            }

            rows.Add(new DirectoryImport(name.Length == 0 ? email : name, email, Text(item, "department"), Text(item, "title", "jobTitle"), active));
        }

        return rows;
    }

    private static string Text(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? "";
                if (text.Length > 0)
                {
                    return text;
                }
            }
        }

        return "";
    }

    private static string Clip(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
