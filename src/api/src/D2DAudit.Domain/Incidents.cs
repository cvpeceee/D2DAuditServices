namespace D2DAudit.Domain;

public sealed class SecurityIncident
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Severity { get; set; } = "Medium";

    public string Status { get; set; } = "Open";

    public string Description { get; set; } = "";

    public string ReporterName { get; set; } = "";

    public string ReporterEmail { get; set; } = "";

    public DateTimeOffset ReportedAt { get; set; }

    public DateOnly SlaDue { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public string RootCause { get; set; } = "";

    public string ControlCode { get; set; } = "";

    public Guid? EvidenceId { get; set; }

    public List<IncidentEvent> Timeline { get; set; } = [];
}

public sealed class IncidentEvent
{
    public Guid Id { get; set; }

    public Guid IncidentId { get; set; }

    public SecurityIncident? Incident { get; set; }

    public string Kind { get; set; } = "Note";

    public string Detail { get; set; } = "";

    public string AuthorName { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public static class IncidentRules
{
    public const string TestName = "Incidents resolved within SLA";

    public static int Days(string severity) => severity switch
    {
        "Critical" => 1,
        "High" => 3,
        "Medium" => 7,
        "Low" => 14,
        _ => 7,
    };

    public static bool WithinSla(string status, DateOnly due, DateTimeOffset? resolvedAt, DateOnly today)
    {
        if (status == "Resolved")
        {
            return resolvedAt is DateTimeOffset resolved && DateOnly.FromDateTime(resolved.UtcDateTime) <= due;
        }

        return today <= due;
    }

    public static CheckResult Evaluate(
        IEnumerable<(string Code, string Status, DateOnly Due, DateTimeOffset? ResolvedAt)> incidents,
        DateOnly today)
    {
        var rows = incidents.ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, "No incidents are on the register.");
        }

        var late = rows.Where(item => !WithinSla(item.Status, item.Due, item.ResolvedAt, today)).Select(item => item.Code).ToArray();
        return late.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{rows.Length} incidents are inside their SLA.")
            : new CheckResult(TestOutcome.Fail, $"Incidents missed the SLA: {string.Join(", ", late)}.");
    }
}
