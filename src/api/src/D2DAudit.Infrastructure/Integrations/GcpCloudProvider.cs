#pragma warning disable S1075 // Google Cloud control-plane hosts are fixed public endpoints.
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class GcpCloudProvider(HttpClient http, ILogger<GcpCloudProvider> logger) : ICloudProvider
{
    public string ProviderKey => "gcp";

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
            return Result<ProviderConnect>.Fail("gcp_credentials_required");
        }

        var token = await TokenAsync(credentials, cancellationToken);
        var status = token is null ? "pending" : "connected";
        var hint = token is null
            ? "The service account could not get a token. Grant Viewer and Logging Viewer on the project."
            : "Read-only collection uses the service account. The private key is stored encrypted.";
        return Result<ProviderConnect>.Ok(new ProviderConnect(
            status,
            credentials.ProjectId,
            null,
            credentials.ProjectId,
            credentials.ClientEmail,
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
            var token = await TokenAsync(credentials, cancellationToken);
            if (token is null)
            {
                return Result<ProviderCollect>.Fail("gcp_collect_failed");
            }

            var project = Uri.EscapeDataString(credentials.ProjectId);
            var resources = new List<NormalizedResource>();
            resources.AddRange(CloudInventory.GcpBuckets(await GetAsync(token, $"https://storage.googleapis.com/storage/v1/b?project={project}", cancellationToken)));
            resources.AddRange(CloudInventory.GcpKms(await GetAsync(token, $"https://cloudkms.googleapis.com/v1/projects/{project}/locations/-/keyRings", cancellationToken)));
            resources.AddRange(CloudInventory.GcpAudit(await GetAsync(token, $"https://logging.googleapis.com/v2/projects/{project}/sinks", cancellationToken)));
            resources.AddRange(CloudInventory.GcpIam(await PostAsync(token, $"https://cloudresourcemanager.googleapis.com/v1/projects/{project}:getIamPolicy", cancellationToken)));
            return Result<ProviderCollect>.Ok(new ProviderCollect(resources));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or CryptographicException or InvalidOperationException)
        {
            logger.LogWarning(exception, "GCP collection failed. {ExceptionType}", exception.GetType().Name);
            return Result<ProviderCollect>.Fail("gcp_collect_failed");
        }
    }

    private async Task<string?> TokenAsync(Credentials credentials, CancellationToken cancellationToken)
    {
        var assertion = Sign(credentials);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        });
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GCP token request was refused. {StatusCode}", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
    }

    private static string Sign(Credentials credentials)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            iss = credentials.ClientEmail,
            scope = "https://www.googleapis.com/auth/cloud-platform",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600,
        }));
        var signingInput = header + "." + payload;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credentials.PrivateKey.Replace("\\n", "\n", StringComparison.Ordinal));
#pragma warning disable S5542 // Google service-account JWTs require RSASSA-PKCS1-v1_5, not PSS.
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
#pragma warning restore S5542
        return signingInput + "." + Base64Url(signature);
    }

    private async Task<string> GetAsync(string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string> PostAsync(string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string Base64Url(string text) => Base64Url(Encoding.UTF8.GetBytes(text));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ProviderConnect Example(ProviderConnection existing) =>
        new(
            "connected",
            existing.ExternalId,
            null,
            existing.ExternalId,
            existing.Domain,
            null,
            null,
            null,
            null,
            null,
            "Example fixture. A non-example workspace collects with a service account.");

    private sealed record Credentials(string ProjectId, string ClientEmail, string PrivateKey)
    {
        public bool Complete => ProjectId.Length > 0 && ClientEmail.Contains('@') && PrivateKey.Contains("PRIVATE KEY", StringComparison.Ordinal);

        public string ToJson() =>
            JsonSerializer.Serialize(new { projectId = ProjectId, clientEmail = ClientEmail, privateKey = PrivateKey });

        public static Credentials Read(ConnectIntegrationRequest request, ProviderConnection? existing)
        {
            var stored = Parse(existing?.Secret);
            return new Credentials(
                First(request.ProjectId, stored.ProjectId),
                First(request.ClientId, stored.ClientEmail),
                First(request.ClientSecret, stored.PrivateKey));
        }

        public static Credentials Parse(string? secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return new Credentials("", "", "");
            }

            try
            {
                using var document = JsonDocument.Parse(secret);
                var root = document.RootElement;
                return new Credentials(Text(root, "projectId"), Text(root, "clientEmail"), Text(root, "privateKey"));
            }
            catch (JsonException)
            {
                return new Credentials("", "", "");
            }
        }

        private static string First(string? next, string current) =>
            string.IsNullOrWhiteSpace(next) ? current : next.Trim();

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
