using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Operations;

public sealed class ConfigurableIssueTracker(
    HttpClient http,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ConfigurableIssueTracker> logger) : IIssueTracker
{
    public async Task<string> CreateIssueAsync(string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var project = string.IsNullOrWhiteSpace(target) ? "SEC" : target.Trim();
        var jiraBase = First(configuration["JIRA_BASE_URL"], configuration["Integrations:Jira:BaseUrl"]);
        var jiraEmail = First(configuration["JIRA_USER_EMAIL"], configuration["Integrations:Jira:UserEmail"]);
        var jiraToken = First(configuration["JIRA_API_TOKEN"], configuration["Integrations:Jira:ApiToken"]);
        if (!string.IsNullOrWhiteSpace(jiraBase) && !string.IsNullOrWhiteSpace(jiraEmail) && !string.IsNullOrWhiteSpace(jiraToken) && SafeUrl.IsHttpOrHttps(jiraBase))
        {
            return await CreateJiraIssueAsync(jiraBase.TrimEnd('/'), jiraEmail, jiraToken, project, summary, detail, cancellationToken);
        }

        var linearKey = First(configuration["LINEAR_API_KEY"], configuration["Integrations:Linear:ApiKey"]);
        var linearEndpoint = First(configuration["LINEAR_ENDPOINT"], configuration["Integrations:Linear:Endpoint"]);
        if (!string.IsNullOrWhiteSpace(linearKey) && !string.IsNullOrWhiteSpace(linearEndpoint) && SafeUrl.IsHttpOrHttps(linearEndpoint))
        {
            return await CreateLinearIssueAsync(linearEndpoint, linearKey, project, summary, detail, cancellationToken);
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return "DEMO-" + project + "-" + Guid.NewGuid().ToString("N")[..8];
        }

        logger.LogWarning("Issue tracker is not configured in {Environment}; skipping outbound ticket for target {Target}.", environment.EnvironmentName, project);
        return "unconfigured:" + project;
    }

    private async Task<string> CreateJiraIssueAsync(
        string baseUrl,
        string email,
        string token,
        string project,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            fields = new
            {
                project = new { key = project },
                summary = summary.Length > 240 ? summary[..240] : summary,
                issuetype = new { name = "Task" },
                description = new
                {
                    type = "doc",
                    version = 1,
                    content = new[]
                    {
                        new
                        {
                            type = "paragraph",
                            content = new[] { new { type = "text", text = detail } },
                        },
                    },
                },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/rest/api/3/issue");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Jira issue creation returned status {StatusCode}.", (int)response.StatusCode);
                return $"jira-error:{(int)response.StatusCode}";
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
            {
                return key.GetString() ?? ("JIRA-" + project);
            }

            return "JIRA-" + project;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Jira issue creation network failure.");
            return "jira-error:network";
        }
    }

    private async Task<string> CreateLinearIssueAsync(
        string endpoint,
        string apiKey,
        string teamId,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        var query = new
        {
            query = "mutation($teamId:String!,$title:String!,$description:String!){issueCreate(input:{teamId:$teamId,title:$title,description:$description}){issue{identifier}}}",
            variables = new { teamId, title = summary, description = detail },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Linear issue creation returned status {StatusCode}.", (int)response.StatusCode);
                return $"linear-error:{(int)response.StatusCode}";
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("issueCreate", out var created)
                && created.TryGetProperty("issue", out var issue)
                && issue.TryGetProperty("identifier", out var identifier)
                && identifier.ValueKind == JsonValueKind.String)
            {
                return identifier.GetString() ?? "LINEAR";
            }

            return "LINEAR";
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Linear issue creation network failure.");
            return "linear-error:network";
        }
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class ConfigurableNotifier(
    HttpClient http,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ConfigurableNotifier> logger) : INotifier
{
    public async Task<string> SendAsync(string channel, string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(channel) ? "notice" : channel.Trim().ToLowerInvariant();
        if (name == "slack")
        {
            var webhook = First(configuration["SLACK_WEBHOOK_URL"], configuration["Integrations:Slack:WebhookUrl"]);
            if (!string.IsNullOrWhiteSpace(webhook) && SafeUrl.IsHttpOrHttps(webhook))
            {
                return await PostWebhookAsync("slack", webhook, new { text = $"*[{target}] {summary}*\n{detail}" }, cancellationToken);
            }
        }
        else if (name == "teams")
        {
            var webhook = First(configuration["TEAMS_WEBHOOK_URL"], configuration["Integrations:Teams:WebhookUrl"]);
            if (!string.IsNullOrWhiteSpace(webhook) && SafeUrl.IsHttpOrHttps(webhook))
            {
                return await PostWebhookAsync("teams", webhook, new { title = summary, text = $"Target: {target}\n\n{detail}" }, cancellationToken);
            }
        }
        else if (name == "email")
        {
            var resendKey = First(configuration["RESEND_API_KEY"], configuration["Integrations:Email:ResendApiKey"]);
            var resendEndpoint = First(configuration["RESEND_ENDPOINT"], configuration["Integrations:Email:ResendEndpoint"]);
            var fromAddress = First(configuration["EMAIL_FROM"], configuration["Integrations:Email:From"]) ?? "compliance@d2daudit.local";
            if (!string.IsNullOrWhiteSpace(resendKey) && !string.IsNullOrWhiteSpace(resendEndpoint) && SafeUrl.IsHttpOrHttps(resendEndpoint) && target.Contains('@', StringComparison.Ordinal))
            {
                return await SendResendEmailAsync(resendEndpoint, resendKey, fromAddress, target, summary, detail, cancellationToken);
            }

            var smtpHost = First(configuration["SMTP_HOST"], configuration["Integrations:Email:SmtpHost"]);
            if (!string.IsNullOrWhiteSpace(smtpHost) && target.Contains('@', StringComparison.Ordinal))
            {
                return await SendSmtpEmailAsync(smtpHost, fromAddress, target, summary, detail, cancellationToken);
            }
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return name + ":" + Guid.NewGuid().ToString("N")[..8];
        }

        logger.LogWarning("Notifier channel {Channel} is not configured in {Environment}.", name, environment.EnvironmentName);
        return "unconfigured:" + name;
    }

    private async Task<string> PostWebhookAsync(string channel, string url, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Channel} webhook delivery returned status {StatusCode}.", channel, (int)response.StatusCode);
                return $"{channel}-error:{(int)response.StatusCode}";
            }

            return $"{channel}:delivered:{Guid.NewGuid().ToString("N")[..8]}";
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "{Channel} webhook delivery network failure.", channel);
            return $"{channel}-error:network";
        }
    }

    private async Task<string> SendResendEmailAsync(
        string endpoint,
        string apiKey,
        string from,
        string to,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { from, to = new[] { to }, subject = summary, text = detail }),
            Encoding.UTF8,
            "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Resend email delivery returned status {StatusCode}.", (int)response.StatusCode);
                return $"email-error:{(int)response.StatusCode}";
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return "email:" + id.GetString();
            }

            return "email:delivered";
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Resend email delivery network failure.");
            return "email-error:network";
        }
    }

    private async Task<string> SendSmtpEmailAsync(
        string host,
        string from,
        string to,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        var port = int.TryParse(First(configuration["SMTP_PORT"], configuration["Integrations:Email:SmtpPort"]), out var parsed) ? parsed : 587;
        var user = First(configuration["SMTP_USER"], configuration["Integrations:Email:SmtpUser"]);
        var password = First(configuration["SMTP_PASSWORD"], configuration["Integrations:Email:SmtpPassword"]);
        try
        {
            using var client = new SmtpClient(host, port) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            using var message = new MailMessage(from, to, summary, detail);
            await client.SendMailAsync(message, cancellationToken);
            return "smtp:" + Guid.NewGuid().ToString("N")[..8];
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
        {
            logger.LogWarning(exception, "SMTP email delivery failed.");
            return "email-error:smtp";
        }
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class AutomationDispatcher(
    TenantContext tenant,
    TenantDatabases databases,
    IIssueTracker issues,
    INotifier notifier,
    TimeProvider time) : IAutomationDispatcher
{
    public Task DispatchAsync(string trigger, string summary, string detail, CancellationToken cancellationToken) =>
        tenant.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(tenant.DatabaseName)
            ? Task.CompletedTask
            : DispatchForTenantAsync(tenant.TenantId, tenant.DatabaseName, trigger, summary, detail, cancellationToken);

    public async Task DispatchForTenantAsync(
        Guid tenantId,
        string databaseName,
        string trigger,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        await using var db = databases.Create(databaseName);
        var rules = await db.AutomationRules.Where(rule => rule.Enabled && rule.Trigger == trigger).ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var rule in rules)
        {
            var external = rule.Action == "Jira"
                ? await issues.CreateIssueAsync(rule.Target, summary, detail, cancellationToken)
                : await notifier.SendAsync(rule.Action, rule.Target, summary, detail, cancellationToken);
            db.Set<AutomationDelivery>().Add(new AutomationDelivery
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                Trigger = trigger,
                Action = rule.Action,
                Target = rule.Target,
                Summary = summary.Length > 300 ? summary[..300] : summary,
                ExternalId = external.Length > 80 ? external[..80] : external,
                At = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
