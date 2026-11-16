namespace D2DAudit.Domain;

public static class BackgroundRules
{
    public const int NewHireDays = 90;

    public const string TestName = "New hires have a background check";

    public static bool IsNewHire(PersonState state, DateOnly? hireOn, DateOnly today) =>
        state == PersonState.Active
        && hireOn is DateOnly hired
        && hired <= today
        && hired > today.AddDays(-NewHireDays);

    public static bool Satisfied(string status, bool excluded, string? reason) =>
        string.Equals(status, "Clear", StringComparison.OrdinalIgnoreCase)
        || (excluded && !string.IsNullOrWhiteSpace(reason));

    public static CheckResult Evaluate(
        IEnumerable<(PersonState State, DateOnly? HireOn, string Status, bool Excluded, string Reason, string Name)> people,
        DateOnly today)
    {
        var hires = people.Where(person => IsNewHire(person.State, person.HireOn, today)).ToArray();
        if (hires.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, "No new hires in the last 90 days.");
        }

        var missing = hires
            .Where(person => !Satisfied(person.Status, person.Excluded, person.Reason))
            .Select(person => person.Name)
            .ToArray();
        if (missing.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, $"{hires.Length} new hires have a background check or a recorded exclusion.");
        }

        var shown = missing.Take(12).ToArray();
        var extra = missing.Length - shown.Length;
        var names = extra > 0 ? string.Join(", ", shown) + $" and {extra} more" : string.Join(", ", shown);
        return new CheckResult(TestOutcome.Fail, $"New hires have no background check: {names}.");
    }
}

public static class AuditWindow
{
    public static bool Contains(DateOnly start, DateOnly end, DateTimeOffset? collected)
    {
        if (collected is null || end < start)
        {
            return false;
        }

        var day = DateOnly.FromDateTime(collected.Value.UtcDateTime);
        return day >= start && day <= end;
    }
}

public static class VulnerabilityCalendar
{
    public static int Days(string severity, int criticalDays, int highDays, int mediumDays, int lowDays) => severity switch
    {
        "Critical" => criticalDays,
        "High" => highDays,
        "Medium" => mediumDays,
        "Low" => lowDays,
        _ => mediumDays,
    };

    public static DateOnly Due(DateTimeOffset firstSeen, string severity, int criticalDays, int highDays, int mediumDays, int lowDays) =>
        DateOnly.FromDateTime(firstSeen.UtcDateTime).AddDays(Days(severity, criticalDays, highDays, mediumDays, lowDays));
}
