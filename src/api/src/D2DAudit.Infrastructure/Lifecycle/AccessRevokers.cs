using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Lifecycle;

/// <summary>Records a deterministic receipt without calling any identity provider.</summary>
public sealed class MockAccessRevoker : IAccessRevoker
{
    public Task<AccessRevocationResult> RevokeAsync(AccessRevocationRequest request, CancellationToken cancellationToken)
    {
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "SSO" : request.Provider;
        var seed = $"{request.PersonEmail.ToLowerInvariant()}|{provider}|{request.AccountName}|{request.StepCode}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..16];
        var account = request.AccountName.Length > 0 ? $" ({request.AccountName})" : "";
        return Task.FromResult(new AccessRevocationResult(
            true,
            "mock",
            $"mock-revoke-{digest}",
            $"Mock revocation recorded for {request.PersonEmail} at {provider}{account}."));
    }
}

/// <summary>
/// Uses Okta for single sign-on revocation when OKTA_DOMAIN and OKTA_API_TOKEN are set.
/// Every other provider, and every provider when unconfigured, falls back to the mock receipt.
/// </summary>
public sealed class ConfigurableAccessRevoker(
    HttpClient http,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ConfigurableAccessRevoker> logger) : IAccessRevoker
{
    private readonly MockAccessRevoker mock = new();

    public static bool OktaConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["OKTA_DOMAIN"]) && !string.IsNullOrWhiteSpace(configuration["OKTA_API_TOKEN"]);

    public async Task<AccessRevocationResult> RevokeAsync(AccessRevocationRequest request, CancellationToken cancellationToken)
    {
        var sso = request.StepCode == "revoke-sso" || string.Equals(request.Provider, "Okta", StringComparison.OrdinalIgnoreCase);
        if (!sso || !OktaConfigured(configuration))
        {
            return await mock.RevokeAsync(request, cancellationToken);
        }

        var domain = configuration["OKTA_DOMAIN"]!.Trim().TrimEnd('/');
        if (!domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            domain = "https://" + domain;
        }

        var url = $"{domain}/api/v1/users/{Uri.EscapeDataString(request.PersonEmail)}/lifecycle/suspend";
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.Authorization = new AuthenticationHeaderValue("SSWS", configuration["OKTA_API_TOKEN"]);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(message, cancellationToken);
            var ok = response.IsSuccessStatusCode;
            var status = (int)response.StatusCode;
            return new AccessRevocationResult(
                ok,
                "okta-live",
                $"okta-{status}-{time.GetUtcNow():yyyyMMddHHmmss}",
                ok ? $"Okta suspended {request.PersonEmail}." : $"Okta returned {status} for {request.PersonEmail}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Okta revocation failed for a leaver.");
            return new AccessRevocationResult(false, "okta-live", "", "Okta could not be reached. Retry the revocation.");
        }
    }
}
