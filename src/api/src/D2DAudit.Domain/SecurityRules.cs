using System.Net;
using System.Net.Sockets;

namespace D2DAudit.Domain;

public static class SafeUrl
{
    public static bool IsHttpOrHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPublicHttpOrHttps(string? value)
    {
        if (!IsHttpOrHttps(value) || !Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0
            || host == "localhost"
            || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.EndsWith(".localdomain", StringComparison.Ordinal))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPublicIpAddress(ip);
        }

        return host.Contains('.', StringComparison.Ordinal);
    }

    public static bool IsPublicIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 198 && bytes[1] is 18 or 19)
                || bytes[0] >= 224)
            {
                return false;
            }

            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public static bool IsSameOrigin(string? candidate, string? baseUrl)
    {
        if (!IsHttpOrHttps(candidate) || !IsHttpOrHttps(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(candidate!.Trim(), UriKind.Absolute, out var target)
            || !Uri.TryCreate(baseUrl!.Trim(), UriKind.Absolute, out var origin))
        {
            return false;
        }

        return string.Equals(target.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && target.Port == origin.Port;
    }
}

public static class CsvSafety
{
    public static string EscapeCell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && IsFormulaPrefix(text[0]))
        {
            text = "'" + text;
        }

        var escaped = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static bool IsFormulaPrefix(char ch) =>
        ch is '=' or '+' or '-' or '@' or '\t' or '\r';
}

public static class TaskRecurrence
{
    public static DateOnly? NextDue(DateOnly currentDue, string? recurrence) => recurrence switch
    {
        "Weekly" => currentDue.AddDays(7),
        "Monthly" => currentDue.AddMonths(1),
        "Quarterly" => currentDue.AddMonths(3),
        "Annual" => currentDue.AddYears(1),
        _ => null,
    };
}

public static class RedirectPolicy
{
    public static bool IsAllowed(string? redirectUri, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return true;
        }

        if (!SafeUrl.IsHttpOrHttps(redirectUri) || !Uri.TryCreate(redirectUri.Trim(), UriKind.Absolute, out var target))
        {
            return false;
        }

        if (allowed.Count == 0)
        {
            return false;
        }

        foreach (var entry in allowed)
        {
            if (string.IsNullOrWhiteSpace(entry) || !Uri.TryCreate(entry.Trim(), UriKind.Absolute, out var permitted))
            {
                continue;
            }

            if (string.Equals(target.Scheme, permitted.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.Host, permitted.Host, StringComparison.OrdinalIgnoreCase)
                && target.Port == permitted.Port)
            {
                var permittedPath = permitted.AbsolutePath.TrimEnd('/');
                if (permittedPath.Length == 0
                    || target.AbsolutePath.Equals(permitted.AbsolutePath, StringComparison.OrdinalIgnoreCase)
                    || target.AbsolutePath.StartsWith(permittedPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
