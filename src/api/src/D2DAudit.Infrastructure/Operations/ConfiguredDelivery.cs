using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Operations;

public sealed partial class ConfiguredIssueTracker(HttpClient http, IConfiguration configuration, ILogger<ConfiguredIssueTracker> logger) : IIssueTracker
{
    public async Task<string> CreateIssueAsync(string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["JIRA_BASE_URL"];
        var email = configuration["JIRA_EMAIL"];
        var token = configuration["JIRA_API_TOKEN"];
        var project = string.IsNullOrWhiteSpace(target) ? configuration["JIRA_PROJECT"] : target.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(project) || !ProjectKey().IsMatch(project))
        {
            return "LOCAL-" + Guid.NewGuid().ToString("N")[..8];
        }

        var body = JsonSerializer.Serialize(new
        {
            fields = new
            {
                project = new { key = project },
                summary = Clip(summary, 200),
                description = Clip(detail, 2000),
                issuetype = new { name = "Task" },
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/rest/api/2/issue");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(email + ":" + token)));
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Jira issue create failed with status {StatusCode}.", (int)response.StatusCode);
            return "LOCAL-" + Guid.NewGuid().ToString("N")[..8];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var key = document.RootElement.TryGetProperty("key", out var value) ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(key) ? "LOCAL-" + Guid.NewGuid().ToString("N")[..8] : key;
    }

    private static string Clip(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,10}$")]
    private static partial Regex ProjectKey();
}

public sealed class ConfiguredNotifier(HttpClient http, IConfiguration configuration, ILogger<ConfiguredNotifier> logger) : INotifier
{
    public async Task<string> SendAsync(string channel, string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var name = (channel ?? "").Trim();
        if (name.Equals("Slack", StringComparison.OrdinalIgnoreCase))
        {
            return await SlackAsync(summary, detail, cancellationToken);
        }

        if (name.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            return await MailAsync(target, summary, detail, cancellationToken);
        }

        if (name.Equals("Teams", StringComparison.OrdinalIgnoreCase))
        {
            return await WebhookAsync(configuration["TEAMS_WEBHOOK_URL"], summary, detail, "teams", cancellationToken);
        }

        return "local:" + Guid.NewGuid().ToString("N")[..8];
    }

    private async Task<string> SlackAsync(string summary, string detail, CancellationToken cancellationToken)
    {
        var url = configuration["SLACK_WEBHOOK_URL"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        var payload = JsonSerializer.Serialize(new { text = Clip(summary + "\n" + detail, 3000) });
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Slack webhook failed with status {StatusCode}.", (int)response.StatusCode);
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        return "slack:" + Guid.NewGuid().ToString("N")[..8];
    }

    private async Task<string> WebhookAsync(string? url, string summary, string detail, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        var payload = JsonSerializer.Serialize(new { text = Clip(summary + "\n" + detail, 3000) });
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Webhook failed with status {StatusCode}.", (int)response.StatusCode);
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        return prefix + ":" + Guid.NewGuid().ToString("N")[..8];
    }

    private async Task<string> MailAsync(string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var host = configuration["SMTP_HOST"];
        var from = configuration["SMTP_FROM"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(target) || !target.Contains('@'))
        {
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        var port = int.TryParse(configuration["SMTP_PORT"], out var parsed) ? parsed : 587;
        using var message = new MailMessage(from, target, Clip(summary, 200), Clip(detail, 4000));
        using var client = new SmtpClient(host, port) { EnableSsl = true };
        var user = configuration["SMTP_USER"];
        var secret = configuration["SMTP_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(secret))
        {
            client.Credentials = new NetworkCredential(user, secret);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException exception)
        {
            logger.LogWarning(exception, "Email delivery failed.");
            return "local:" + Guid.NewGuid().ToString("N")[..8];
        }

        return "email:" + Guid.NewGuid().ToString("N")[..8];
    }

    private static string Clip(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }
}
