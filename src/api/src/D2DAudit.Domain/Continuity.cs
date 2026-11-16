namespace D2DAudit.Domain;

public sealed class ImpactAnalysis
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Criticality { get; set; } = "Medium";

    public int RtoHours { get; set; }

    public int RpoHours { get; set; }

    public string OwnerName { get; set; } = "";

    public string ControlCode { get; set; } = "";

    public string Notes { get; set; } = "";
}

public sealed class ContinuityPlan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Kind { get; set; } = "Continuity";

    public string Body { get; set; } = "";

    public string ControlCode { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RecoveryExercise
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public Guid? PlanId { get; set; }

    public string Result { get; set; } = "Pass";

    public string Notes { get; set; } = "";

    public string ControlCode { get; set; } = "";

    public DateTimeOffset RanAt { get; set; }
}

public static class ContinuityRules
{
    public const string TestName = "Recovery exercises are recorded";

    public const string ControlCode = "BC-01";

    public static CheckResult Evaluate(IEnumerable<(string Name, string Result)> exercises)
    {
        var rows = exercises.ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.Fail, "No recovery exercise is on the log.");
        }

        var failed = rows.Where(item => item.Result != "Pass").Select(item => item.Name).ToArray();
        return failed.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{rows.Length} recovery exercises passed.")
            : new CheckResult(TestOutcome.Fail, $"Exercises failed: {string.Join(", ", failed)}.");
    }
}
