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
            return true;
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
