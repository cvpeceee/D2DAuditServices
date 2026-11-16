namespace D2DAudit.Domain;

public sealed class ChangeRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Source { get; set; } = "manual";

    public string ExternalKey { get; set; } = "";

    public string Repository { get; set; } = "";

    public string AuthorName { get; set; } = "";

    public string Status { get; set; } = "Open";

    public string ApproverName { get; set; } = "";

    public DateTimeOffset? ApprovedAt { get; set; }

    public string DeployRef { get; set; } = "";

    public DateTimeOffset? DeployedAt { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
}

public static class ChangeRules
{
    public const string TestName = "production changes are reviewed";

    public const string ControlCode = "CM-11";

    public static CheckResult Evaluate(IEnumerable<(string Code, DateTimeOffset? ApprovedAt, DateTimeOffset? DeployedAt)> changes)
    {
        var deployed = changes.Where(item => item.DeployedAt is not null).ToArray();
        if (deployed.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, "No production changes are on the log.");
        }

        var late = deployed.Where(item => item.ApprovedAt is null).Select(item => item.Code).ToArray();
        return late.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{deployed.Length} deployed changes have an approval.")
            : new CheckResult(TestOutcome.Fail, $"Deployed without approval: {string.Join(", ", late)}.");
    }
}
