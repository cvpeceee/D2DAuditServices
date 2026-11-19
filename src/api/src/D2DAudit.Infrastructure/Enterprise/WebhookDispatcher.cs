using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Identity;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Storage;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Enterprise;

public sealed class WebhookSecretProtector(SecretBox secrets, IHostEnvironment environment)
{
    private const string DevPrefix = "devgcm:";

    public string? Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        if (secrets.CanProtect)
        {
            return secrets.Encrypt(plaintext);
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return null;
        }

        var key = SHA256.HashData(Encoding.UTF8.GetBytes(DevOnlySigningKey.Value));
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return DevPrefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        if (stored.StartsWith(DevPrefix, StringComparison.Ordinal))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                return null;
            }

            var key = SHA256.HashData(Encoding.UTF8.GetBytes(DevOnlySigningKey.Value));
            var bytes = Convert.FromBase64String(stored[DevPrefix.Length..]);
            var nonce = bytes[..12];
            var tag = bytes[12..28];
            var cipher = bytes[28..];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }

        return secrets.CanProtect ? secrets.Decrypt(stored) : null;
    }
}

public sealed class WebhookDispatcher(
    HttpClient http,
    TenantDatabases databases,
    WebhookSecretProtector protector,
    TimeProvider time,
    ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    public async Task<IReadOnlyList<string>> DispatchAsync(
        string databaseName,
        string eventName,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || !DatabaseNames.IsSafe(databaseName))
        {
            return [];
        }

        await using var db = databases.Create(databaseName);
        var hooks = await db.Webhooks.AsNoTracking()
            .Where(item => item.Enabled)
            .ToListAsync(cancellationToken);
        var matched = hooks.Where(hook => WebhookSigning.MatchesEvent(hook.Events, eventName)).ToArray();
        if (matched.Length == 0)
        {
            return [];
        }

        var results = new List<string>(matched.Length);
        var now = time.GetUtcNow();
        foreach (var hook in matched)
        {
            var outcome = await SendSingleAsync(hook, eventName, summary, detail, cancellationToken);
            var externalId = outcome.Delivered
                ? $"webhook:{outcome.StatusCode.ToString(CultureInfo.InvariantCulture)}:{Guid.NewGuid().ToString("N")[..8]}"
                : $"webhook-error:{outcome.Detail}";
            results.Add(externalId);
            db.AutomationDeliveries.Add(new AutomationDelivery
            {
                Id = Guid.NewGuid(),
                RuleId = hook.Id,
                Trigger = eventName.Length > 32 ? eventName[..32] : eventName,
                Action = "Webhook",
                Target = hook.Name.Length > 200 ? hook.Name[..200] : hook.Name,
                Summary = summary.Length > 300 ? summary[..300] : summary,
                ExternalId = externalId.Length > 80 ? externalId[..80] : externalId,
                At = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<WebhookTestResultDto> SendSingleAsync(
        WebhookSubscription webhook,
        string eventName,
        string summary,
        string detail,
        CancellationToken cancellationToken)
    {
        if (!SafeUrl.IsPublicHttpOrHttps(webhook.Url) || !Uri.TryCreate(webhook.Url.Trim(), UriKind.Absolute, out var uri))
        {
            return new WebhookTestResultDto(false, 0, eventName, "", "blocked_ssrf_url");
        }

        if (!await ResolvesToPublicOnlyAsync(uri.DnsSafeHost, cancellationToken))
        {
            return new WebhookTestResultDto(false, 0, eventName, "", "blocked_private_dns");
        }

        var secret = protector.Unprotect(webhook.SigningSecretEncrypted);
        if (string.IsNullOrEmpty(secret))
        {
            return new WebhookTestResultDto(false, 0, eventName, "", "signing_secret_unavailable");
        }

        var now = time.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            summary,
            detail,
            sentAt = now,
        });
        var signature = WebhookSigning.Sign(secret, timestamp, payload);
        var signaturePreview = signature.Length > 28 ? signature[..28] + "…" : signature;

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-D2D-Event", eventName);
        request.Headers.TryAddWithoutValidation("X-D2D-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-D2D-Signature", signature);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webhook {WebhookName} returned HTTP {StatusCode}.", webhook.Name, status);
                return new WebhookTestResultDto(false, status, eventName, signaturePreview, $"http_{status.ToString(CultureInfo.InvariantCulture)}");
            }

            return new WebhookTestResultDto(true, status, eventName, signaturePreview, "delivered");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Webhook {WebhookName} network delivery failed.", webhook.Name);
            return new WebhookTestResultDto(false, 0, eventName, signaturePreview, "network_error");
        }
    }

    private static async Task<bool> ResolvesToPublicOnlyAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return SafeUrl.IsPublicIpAddress(literal);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length > 0 && addresses.All(SafeUrl.IsPublicIpAddress);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
