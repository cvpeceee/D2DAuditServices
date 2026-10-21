using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace D2DAudit.Infrastructure.Identity;

public static class DevOnlySigningKey
{
    public const string Value = "local-development-identity-signing-key-do-not-use-in-production";

    public static string Resolve(string environmentName, string? configured)
    {
        var allowDevKey = environmentName is "Development" or "Testing";
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32)
            {
                throw new InvalidOperationException("LOCAL_IDENTITY_SIGNING_KEY must be at least 32 characters.");
            }

            if (!allowDevKey && configured == Value)
            {
                throw new InvalidOperationException("Refusing the development identity signing key outside Development and Testing.");
            }

            return configured;
        }

        if (allowDevKey)
        {
            return Value;
        }

        throw new InvalidOperationException("LOCAL_IDENTITY_SIGNING_KEY is required.");
    }
}

public static class LocalTokenConstants
{
    public const string Issuer = "d2d-local";

    public const string Audience = "d2d-api";
}

public static class SignedTokenValidator
{
    public const int MaxTokenLength = 16_384;

    public static async Task<AccessTokenIdentity?> ValidateAsync(
        string token,
        TokenValidationParameters parameters,
        string? requiredClientId = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength || token.Count(c => c == '.') != 2)
        {
            return null;
        }

        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        TokenValidationResult result;
        try
        {
            result = await handler.ValidateTokenAsync(token, parameters);
        }
        catch (SecurityTokenException)
        {
            return null;
        }

        if (!result.IsValid)
        {
            return null;
        }

        if (requiredClientId is not null && !string.Equals(Read(result, "client_id"), requiredClientId, StringComparison.Ordinal))
        {
            return null;
        }

        var subject = Read(result, "sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var expires = result.SecurityToken?.ValidTo ?? DateTime.MinValue;
        if (expires == DateTime.MinValue)
        {
            return null;
        }

        return new AccessTokenIdentity(subject, new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)));
    }

    public static TokenValidationParameters LocalParameters(string signingKey) => new()
    {
        ValidIssuer = LocalTokenConstants.Issuer,
        ValidAudience = LocalTokenConstants.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    public static TokenValidationParameters WorkOsParameters(
        IEnumerable<SecurityKey> keys,
        string issuer,
        string? audience)
    {
        var trimmed = issuer.Trim().TrimEnd('/');
        return new TokenValidationParameters
        {
            ValidIssuers = [trimmed, trimmed + "/"],
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = string.IsNullOrWhiteSpace(audience) ? null : audience,
            IssuerSigningKeys = keys.ToArray(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
        };
    }

    public static string CreateLocalToken(string signingKey, string subject, string email, string name, DateTime expiresUtc)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokenConstants.Issuer,
            Audience = LocalTokenConstants.Audience,
            Expires = expiresUtc,
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["email"] = email,
                ["name"] = name,
            },
        });
    }

    public static string? Read(TokenValidationResult result, string name)
    {
        if (!result.Claims.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString(),
        };
    }
}
