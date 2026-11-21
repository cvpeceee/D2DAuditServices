namespace D2DAudit.Infrastructure.Persistence;

public static class DbText
{
    // PostgreSQL LIKE/ILIKE uses backslash as the default escape character.
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static string ContainsPattern(string term) => $"%{EscapeLike(term)}%";
}
