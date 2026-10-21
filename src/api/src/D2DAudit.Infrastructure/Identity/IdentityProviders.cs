using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace D2DAudit.Infrastructure.Identity;

public sealed class WorkOsOptions
{
    public string ApiKey { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string Issuer { get; set; } = "https://api.workos.com";

    public string? Audience { get; set; }

    public string[] RedirectUris { get; set; } = [];
}

public sealed class LocalIdentityProvider(
    ControlPlaneDbContext db,
    PasswordHasher<UserAccount> passwords,
    TimeProvider time) : IIdentityProvider
{
    private readonly string _signingKey = DevOnlySigningKey.Resolve(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        Environment.GetEnvironmentVariable("LOCAL_IDENTITY_SIGNING_KEY"));

    private readonly string _dummyHash = passwords.HashPassword(new UserAccount(), "dummy-password-not-a-user");

    public IdentityMode Mode => IdentityMode.Local;

    public async Task<IdentityLoginResult> PasswordLoginAsync(
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        var hash = user?.PasswordHash ?? _dummyHash;
        var verdict = passwords.VerifyHashedPassword(user ?? new UserAccount(), hash, password);
        if (user is null || user.PasswordHash is null || verdict == PasswordVerificationResult.Failed)
        {
            return IdentityLoginResult.Fail("invalid_credentials");
        }

        var expires = time.GetUtcNow().UtcDateTime.AddHours(12);
        var token = SignedTokenValidator.CreateLocalToken(_signingKey, user.Subject, user.Email, user.DisplayName, expires);
        return IdentityLoginResult.Success(
            token,
            null,
            new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)),
            new IdentityPrincipal(user.Subject, user.Email, user.DisplayName));
    }

    public Task<IdentityLoginResult> CompleteMfaAsync(
        string pendingToken,
        string code,
        string? challengeId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityLoginResult.Fail("local_identity_mode"));

    public Task<AuthorizationRedirect> GetAuthorizationUrlAsync(ExternalLoginStart start, CancellationToken cancellationToken) =>
        Task.FromResult(new AuthorizationRedirect(false, null, "local_identity_mode"));

    public Task<IdentityLoginResult> CompleteAuthorizationCodeAsync(
        string code,
        string redirectUri,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityLoginResult.Fail("local_identity_mode"));

    public Task<IdentityLoginResult> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityLoginResult.Fail("refresh_not_supported"));

    public Task<AccessTokenIdentity?> ValidateAccessTokenAsync(string token, CancellationToken cancellationToken) =>
        SignedTokenValidator.ValidateAsync(token, SignedTokenValidator.LocalParameters(_signingKey));

    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken) =>
        Task.FromException(new LocalPasswordResetException());

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public sealed class LocalPasswordResetException : Exception;

public sealed partial class WorkOsIdentityProvider(
    HttpClient http,
    IOptions<WorkOsOptions> options,
    ControlPlaneDbContext db) : IIdentityProvider
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly WorkOsOptions _options = options.Value;
    private readonly SemaphoreSlim _jwksGate = new(1, 1);
    private IReadOnlyCollection<SecurityKey>? _keys;
    private DateTimeOffset _keysFetched;

    public IdentityMode Mode => IdentityMode.WorkOs;

    public async Task<IdentityLoginResult> PasswordLoginAsync(
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ApiKey,
            ["grant_type"] = "password",
            ["email"] = email.Trim(),
            ["password"] = password,
        };
        AddClientContext(payload, ip, userAgent);
        return await AuthenticateAsync(payload, cancellationToken);
    }

    public async Task<IdentityLoginResult> CompleteMfaAsync(
        string pendingToken,
        string code,
        string? challengeId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pendingToken) || pendingToken.Length > 4000 || string.IsNullOrWhiteSpace(challengeId))
        {
            return IdentityLoginResult.Fail("mfa_invalid");
        }

        if (!TotpCode().IsMatch(code))
        {
            return IdentityLoginResult.Fail("mfa_invalid");
        }

        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ApiKey,
            ["grant_type"] = "urn:workos:oauth:grant-type:mfa-totp",
            ["code"] = code,
            ["pending_authentication_token"] = pendingToken,
            ["authentication_challenge_id"] = challengeId,
        };
        AddClientContext(payload, ip, userAgent);
        return await AuthenticateAsync(payload, cancellationToken);
    }

    public async Task<AuthorizationRedirect> GetAuthorizationUrlAsync(ExternalLoginStart start, CancellationToken cancellationToken)
    {
        if (!_options.RedirectUris.Contains(start.RedirectUri, StringComparer.Ordinal))
        {
            return new AuthorizationRedirect(false, null, "redirect_not_allowed");
        }

        if (!StateValue().IsMatch(start.State))
        {
            return new AuthorizationRedirect(false, null, "invalid_state");
        }

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = start.RedirectUri,
            ["response_type"] = "code",
            ["state"] = start.State,
        };

        switch (start.Provider.Trim().ToLowerInvariant())
        {
            case "google":
                query["provider"] = "GoogleOAuth";
                break;
            case "microsoft":
                query["provider"] = "MicrosoftOAuth";
                break;
            case "sso":
                if (string.IsNullOrWhiteSpace(start.Email) || !start.Email.Contains('@'))
                {
                    return new AuthorizationRedirect(false, null, "email_required");
                }

                var domain = start.Email.Split('@')[1].Trim().ToLowerInvariant();
                var organizationId = start.OrganizationId;
                if (string.IsNullOrWhiteSpace(organizationId))
                {
                    organizationId = await db.TenantDomains
                        .Where(d => d.Domain == domain)
                        .Select(d => d.WorkOsOrganizationId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(organizationId))
                {
                    return new AuthorizationRedirect(false, null, "sso_not_configured");
                }

                query["organization_id"] = organizationId;
                query["login_hint"] = start.Email.Trim();
                break;
            default:
                return new AuthorizationRedirect(false, null, "unknown_provider");
        }

        if (!string.IsNullOrWhiteSpace(start.Email) && start.Provider.Trim().ToLowerInvariant() != "sso")
        {
            query["login_hint"] = start.Email.Trim();
        }

        var builder = new StringBuilder("https://api.workos.com/user_management/authorize?");
        var first = true;
        foreach (var pair in query)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
        }

        return new AuthorizationRedirect(true, builder.ToString(), null);
    }

    public Task<IdentityLoginResult> CompleteAuthorizationCodeAsync(
        string code,
        string redirectUri,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (!_options.RedirectUris.Contains(redirectUri, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(code) || code.Length > 2000)
        {
            return Task.FromResult(IdentityLoginResult.Fail("invalid_callback"));
        }

        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ApiKey,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        };
        AddClientContext(payload, ip, userAgent);
        return AuthenticateAsync(payload, cancellationToken);
    }

    public Task<IdentityLoginResult> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 4000)
        {
            return Task.FromResult(IdentityLoginResult.Fail("invalid_refresh"));
        }

        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ApiKey,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        AddClientContext(payload, ip, userAgent);
        return AuthenticateAsync(payload, cancellationToken);
    }

    public async Task<AccessTokenIdentity?> ValidateAccessTokenAsync(string token, CancellationToken cancellationToken)
    {
        var keys = await GetKeysAsync(force: false, cancellationToken);
        var identity = await ValidateWithKeysAsync(token, keys);
        if (identity is not null)
        {
            return identity;
        }

        keys = await GetKeysAsync(force: true, cancellationToken);
        return await ValidateWithKeysAsync(token, keys);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.workos.com/user_management/password_reset");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { email = email.Trim() }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        // The caller always shows the same message. A miss or a WorkOS rejection must not reveal whether the mailbox exists.
        _ = response.IsSuccessStatusCode;
    }

    private Task<AccessTokenIdentity?> ValidateWithKeysAsync(string token, IReadOnlyCollection<SecurityKey> keys)
    {
        if (keys.Count == 0)
        {
            return Task.FromResult<AccessTokenIdentity?>(null);
        }

        var parameters = SignedTokenValidator.WorkOsParameters(keys, _options.Issuer, _options.Audience);
        return SignedTokenValidator.ValidateAsync(token, parameters, _options.ClientId);
    }

    private async Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(bool force, CancellationToken cancellationToken)
    {
        var fresh = _keys is not null && DateTimeOffset.UtcNow - _keysFetched < TimeSpan.FromMinutes(5);
        if (!force && fresh)
        {
            return _keys!;
        }

        await _jwksGate.WaitAsync(cancellationToken);
        try
        {
            fresh = _keys is not null && DateTimeOffset.UtcNow - _keysFetched < TimeSpan.FromMinutes(5);
            if (!force && fresh)
            {
                return _keys!;
            }

            if (force && _keys is not null && DateTimeOffset.UtcNow - _keysFetched < TimeSpan.FromSeconds(30))
            {
                return _keys;
            }

            using var response = await http.GetAsync(
                $"https://api.workos.com/sso/jwks/{Uri.EscapeDataString(_options.ClientId)}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var set = new JsonWebKeySet(body);
            _keys = set.GetSigningKeys().ToArray();
            _keysFetched = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally
        {
            _jwksGate.Release();
        }
    }

    private async Task<IdentityLoginResult> AuthenticateAsync(Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.workos.com/user_management/authenticate");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var ok = JsonSerializer.Deserialize<WorkOsAuthOk>(body, Json);
            if (ok?.AccessToken is null || ok.User?.Id is null || ok.User.Email is null)
            {
                return IdentityLoginResult.Fail("identity_provider_error");
            }

            var validated = await ValidateAccessTokenAsync(ok.AccessToken, cancellationToken);
            if (validated is null || validated.Subject != ok.User.Id)
            {
                return IdentityLoginResult.Fail("identity_provider_error");
            }

            var name = string.Join(' ', new[] { ok.User.FirstName, ok.User.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ok.User.Email;
            }

            return IdentityLoginResult.Success(
                ok.AccessToken,
                ok.RefreshToken,
                validated.ExpiresAt,
                new IdentityPrincipal(ok.User.Id, ok.User.Email.Trim().ToLowerInvariant(), name));
        }

        var error = JsonSerializer.Deserialize<WorkOsError>(body, Json);
        if (error?.Code is "mfa_challenge" or "mfa_enrollment" && !string.IsNullOrWhiteSpace(error.PendingAuthenticationToken))
        {
            if (error.Code == "mfa_enrollment")
            {
                return IdentityLoginResult.Fail("mfa_enrollment_required");
            }

            var factors = (error.AuthenticationFactors ?? [])
                .Where(factor => !string.IsNullOrWhiteSpace(factor.Id) && !string.IsNullOrWhiteSpace(factor.Type))
                .Select(factor => new MfaFactor(factor.Id!, factor.Type!))
                .ToArray();
            var totp = factors.FirstOrDefault(factor => factor.Type.Equals("totp", StringComparison.OrdinalIgnoreCase));
            if (totp is null)
            {
                return IdentityLoginResult.Fail("mfa_factor_unsupported");
            }

            var challengeId = await ChallengeFactorAsync(totp.Id, cancellationToken);
            if (challengeId is null)
            {
                return IdentityLoginResult.Fail("mfa_challenge_failed");
            }

            return IdentityLoginResult.Mfa(error.PendingAuthenticationToken, challengeId, factors);
        }

        return IdentityLoginResult.Fail(error?.Code switch
        {
            "invalid_credentials" or "invalid_grant" or "organization_selection_required" => "invalid_credentials",
            "email_verification_required" => "email_verification_required",
            _ => "invalid_credentials",
        });
    }

    private async Task<string?> ChallengeFactorAsync(string factorId, CancellationToken cancellationToken)
    {
        if (!FactorId().IsMatch(factorId))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.workos.com/auth/factors/{factorId}/challenge");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var challenge = JsonSerializer.Deserialize<WorkOsChallenge>(body, Json);
        return string.IsNullOrWhiteSpace(challenge?.Id) ? null : challenge.Id;
    }

    private static void AddClientContext(Dictionary<string, string> payload, string? ip, string? userAgent)
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            payload["ip_address"] = ip;
        }

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            payload["user_agent"] = userAgent.Length > 512 ? userAgent[..512] : userAgent;
        }
    }

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex TotpCode();

    [GeneratedRegex("^[A-Za-z0-9_-]{16,200}$")]
    private static partial Regex StateValue();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex FactorId();

    private sealed class WorkOsAuthOk
    {
        public WorkOsUser? User { get; set; }

        public string? AccessToken { get; set; }

        public string? RefreshToken { get; set; }
    }

    private sealed class WorkOsUser
    {
        public string? Id { get; set; }

        public string? Email { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }

    private sealed class WorkOsError
    {
        public string? Code { get; set; }

        public string? PendingAuthenticationToken { get; set; }

        public List<WorkOsFactor>? AuthenticationFactors { get; set; }
    }

    private sealed class WorkOsFactor
    {
        public string? Id { get; set; }

        public string? Type { get; set; }
    }

    private sealed class WorkOsChallenge
    {
        public string? Id { get; set; }
    }
}
