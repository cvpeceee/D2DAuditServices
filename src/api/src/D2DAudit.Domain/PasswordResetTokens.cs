using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace D2DAudit.Domain;

/// <summary>
/// Signed, expiring password reset tokens for local identity mode. The token binds a digest of the current
/// password hash, so it stops working once the password changes.
/// </summary>
public static class LocalResetTokens
{
    public static string Create(string signingKey, string email, string passwordHash, DateTimeOffset expires)
    {
        var payload = string.Join('|', email.Trim().ToLowerInvariant(), expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), Stamp(passwordHash));
        var body = Base64Url(Encoding.UTF8.GetBytes(payload));
        return body + "." + Sign(signingKey, body);
    }

    public static string? ReadEmail(string token)
    {
        var parts = Parse(token);
        return parts?.Email;
    }

    public static bool Validate(string signingKey, string token, string email, string passwordHash, DateTimeOffset now)
    {
        var dot = (token ?? "").IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return false;
        }

        var body = token![..dot];
        var expected = Encoding.ASCII.GetBytes(Sign(signingKey, body));
        var actual = Encoding.ASCII.GetBytes(token[(dot + 1)..]);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return false;
        }

        var parts = Parse(token);
        return parts is not null
            && string.Equals(parts.Value.Email, email.Trim().ToLowerInvariant(), StringComparison.Ordinal)
            && parts.Value.Expires > now
            && string.Equals(parts.Value.Stamp, Stamp(passwordHash), StringComparison.Ordinal);
    }

    private static (string Email, DateTimeOffset Expires, string Stamp)? Parse(string token)
    {
        try
        {
            var dot = (token ?? "").IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || token!.Length > 2048)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(FromBase64Url(token[..dot]));
            var fields = text.Split('|');
            if (fields.Length != 3 || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return null;
            }

            return (fields[0], DateTimeOffset.FromUnixTimeSeconds(seconds), fields[2]);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string Stamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

    private static string Sign(string key, string body) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("reset:" + body)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));
    }
}
