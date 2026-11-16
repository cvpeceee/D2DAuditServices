#pragma warning disable S1075 // Azure control-plane hosts are fixed public endpoints.
using System.Net.Http.Headers;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class AzureCloudProvider(HttpClient http, ILogger<AzureCloudProvider> logger) : ICloudProvider
{
    public string ProviderKey => "azure";

    public async Task<Result<ProviderConnect>> ConnectAsync(
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        CancellationToken cancellationToken)
    {
        if (existing?.Example == true)
        {
            return Result<ProviderConnect>.Ok(Example(existing));
        }

        var credentials = Credentials.Read(request, existing);
        if (!credentials.Complete)
        {
            return Result<ProviderConnect>.Fail("azure_credentials_required");
        }

        var token = await TokenAsync(credentials, "https://management.azure.com/.default", cancellationToken);
        var status = token is null ? "pending" : "connected";
        var hint = token is null
            ? "The app registration could not get a token. Grant Reader on the subscription and UserAuthenticationMethod.Read.All for Entra ID."
            : "Read-only collection uses the app registration. The client secret is stored encrypted.";
        return Result<ProviderConnect>.Ok(new ProviderConnect(
            status,
            credentials.TenantId,
            null,
            credentials.SubscriptionId,
            credentials.TenantId,
            null,
            credentials.ToJson(),
            null,
            null,
            null,
            hint));
    }

    public async Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Example)
        {
            return Result<ProviderCollect>.Ok(new ProviderCollect(ExampleSnapshots.For(ProviderKey, connection.Fixture)));
        }

        var credentials = Credentials.Parse(connection.Secret);
        if (!credentials.Complete)
        {
            return Result<ProviderCollect>.Fail("not_connected");
        }

        try
        {
            var arm = await TokenAsync(credentials, "https://management.azure.com/.default", cancellationToken);
            if (arm is null)
            {
                return Result<ProviderCollect>.Fail("azure_collect_failed");
            }

            var resources = new List<NormalizedResource>();
            var subscription = Uri.EscapeDataString(credentials.SubscriptionId);
            resources.AddRange(CloudInventory.AzureStorage(await GetAsync(arm, $"https://management.azure.com/subscriptions/{subscription}/providers/Microsoft.Storage/storageAccounts?api-version=2023-05-01", cancellationToken)));
            resources.AddRange(CloudInventory.AzureVaults(await GetAsync(arm, $"https://management.azure.com/subscriptions/{subscription}/providers/Microsoft.KeyVault/vaults?api-version=2023-07-01", cancellationToken)));
            resources.AddRange(CloudInventory.AzureActivity(await GetAsync(arm, $"https://management.azure.com/subscriptions/{subscription}/providers/Microsoft.Insights/activityLogAlerts?api-version=2020-10-01", cancellationToken)));
            resources.AddRange(CloudInventory.AzureNetwork(await GetAsync(arm, $"https://management.azure.com/subscriptions/{subscription}/providers/Microsoft.Network/networkSecurityGroups?api-version=2023-11-01", cancellationToken)));
            var graph = await TokenAsync(credentials, "https://graph.microsoft.com/.default", cancellationToken);
            if (graph is not null)
            {
                var users = await GetOptionalAsync(graph, "https://graph.microsoft.com/v1.0/reports/authenticationMethods/userRegistrationDetails?$top=200", cancellationToken);
                if (users is not null)
                {
                    resources.AddRange(CloudInventory.EntraUsers(users));
                }
            }

            return Result<ProviderCollect>.Ok(new ProviderCollect(resources));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Azure collection failed. {ExceptionType}", exception.GetType().Name);
            return Result<ProviderCollect>.Fail("azure_collect_failed");
        }
    }

    private async Task<string?> TokenAsync(Credentials credentials, string scope, CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["scope"] = scope,
            ["grant_type"] = "client_credentials",
        });
        using var response = await http.PostAsync(
            "https://login.microsoftonline.com/" + Uri.EscapeDataString(credentials.TenantId) + "/oauth2/v2.0/token",
            body,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure token request was refused. {StatusCode}", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
    }

    private async Task<string> GetAsync(string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string?> GetOptionalAsync(string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Entra ID user registration could not be read. {StatusCode}", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static ProviderConnect Example(ProviderConnection existing) =>
        new(
            "connected",
            existing.ExternalId,
            null,
            existing.Domain,
            existing.Domain,
            null,
            null,
            null,
            null,
            null,
            "Example fixture. A non-example workspace collects with an app registration.");

    private sealed record Credentials(string TenantId, string ClientId, string ClientSecret, string SubscriptionId)
    {
        public bool Complete =>
            TenantId.Length > 0 && ClientId.Length > 0 && ClientSecret.Length > 0 && SubscriptionId.Length > 0;

        public string ToJson() =>
            JsonSerializer.Serialize(new
            {
                tenantId = TenantId,
                clientId = ClientId,
                clientSecret = ClientSecret,
                subscriptionId = SubscriptionId,
            });

        public static Credentials Read(ConnectIntegrationRequest request, ProviderConnection? existing)
        {
            var stored = Parse(existing?.Secret);
            return new Credentials(
                First(request.TenantId, stored.TenantId),
                First(request.ClientId, stored.ClientId),
                First(request.ClientSecret, stored.ClientSecret),
                First(request.SubscriptionId, stored.SubscriptionId));
        }

        public static Credentials Parse(string? secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return new Credentials("", "", "", "");
            }

            try
            {
                using var document = JsonDocument.Parse(secret);
                var root = document.RootElement;
                return new Credentials(Text(root, "tenantId"), Text(root, "clientId"), Text(root, "clientSecret"), Text(root, "subscriptionId"));
            }
            catch (JsonException)
            {
                return new Credentials("", "", "", "");
            }
        }

        private static string First(string? next, string current) =>
            string.IsNullOrWhiteSpace(next) ? current : next.Trim();

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
