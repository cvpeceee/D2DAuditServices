using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Changes;

public sealed partial class JiraChangeFeed(HttpClient http, IConfiguration configuration, ILogger<JiraChangeFeed> logger) : IJiraChangeFeed
{
    public async Task<IReadOnlyList<ExternalChange>> ListAsync(CancellationToken cancellationToken)
    {
        var baseUrl = configuration["JIRA_BASE_URL"];
        var email = configuration["JIRA_EMAIL"];
        var token = configuration["JIRA_API_TOKEN"];
        var project = configuration["JIRA_PROJECT"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(project))
        {
            return [];
        }

        if (!ProjectKey().IsMatch(project))
        {
            logger.LogWarning("Jira project key was rejected.");
            return [];
        }

        var jql = "project = " + project + " AND labels = production-change ORDER BY updated DESC";
        var url = baseUrl.TrimEnd('/') + "/rest/api/3/search/jql?maxResults=20&fields=summary,status&jql=" + Uri.EscapeDataString(jql);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(email + ":" + token)));
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Jira change search failed with status {StatusCode}.", (int)response.StatusCode);
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("issues", out var issues))
        {
            return [];
        }

        var rows = new List<ExternalChange>();
        foreach (var issue in issues.EnumerateArray())
        {
            var key = Text(issue, "key");
            if (key.Length == 0 || !issue.TryGetProperty("fields", out var fields))
            {
                continue;
            }

            var title = Text(fields, "summary");
            var status = fields.TryGetProperty("status", out var statusNode) ? Text(statusNode, "name") : "";
            var approved = IsApproved(status);
            var deployed = status.Equals("Deployed", StringComparison.OrdinalIgnoreCase);
            rows.Add(new ExternalChange(
                "jira",
                key,
                title.Length == 0 ? key : title,
                project,
                "",
                approved,
                approved ? "Jira" : "",
                deployed ? DateTimeOffset.UtcNow : null,
                deployed ? key : ""));
        }

        return rows;
    }

    private static bool IsApproved(string status) =>
        status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Deployed", StringComparison.OrdinalIgnoreCase);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,10}$")]
    private static partial Regex ProjectKey();
}
