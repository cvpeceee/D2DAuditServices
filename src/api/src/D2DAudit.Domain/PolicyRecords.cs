namespace D2DAudit.Domain;

public enum PolicyState
{
    Draft,
    InReview,
    Approved,
    Superseded,
}

public sealed class PolicyDocument
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public List<PolicyVersion> Versions { get; set; } = [];

    public List<PolicyControlLink> Controls { get; set; } = [];
}

public sealed class PolicyVersion
{
    public Guid Id { get; set; }

    public Guid PolicyId { get; set; }

    public PolicyDocument? Policy { get; set; }

    public int Number { get; set; }

    public string Body { get; set; } = "";

    public PolicyState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovedByName { get; set; }

    public List<PolicyAcknowledgement> Acknowledgements { get; set; } = [];
}

public sealed class PolicyControlLink
{
    public Guid PolicyId { get; set; }

    public PolicyDocument? Policy { get; set; }

    public string ControlCode { get; set; } = "";
}

public sealed class PolicyAcknowledgement
{
    public Guid Id { get; set; }

    public Guid PolicyVersionId { get; set; }

    public PolicyVersion? Version { get; set; }

    public string PersonName { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public DateTimeOffset? RemindedAt { get; set; }
}

public sealed record PolicyTemplate(string Code, string Title, string Body, IReadOnlyList<string> ControlCodes);

public static class PolicyTemplates
{
    public static IReadOnlyList<PolicyTemplate> Core { get; } =
    [
        new(
            "POL-ISP",
            "Information security policy",
            "The company names an owner for information security and reviews this policy once a year. Every worker follows the controls mapped to it. An exception is written down and approved before the work proceeds.",
            ["GV-01"]),
        new(
            "POL-ACC",
            "Access control policy",
            "An account is issued to one named person. A second factor is required on every active account. Access is removed when the person leaves, and a shared password is not used.",
            ["AC-07"]),
        new(
            "POL-CHG",
            "Change management policy",
            "A change to production is recorded before it ships. The default branch of a production repository stays protected so a second person reviews the change before it merges.",
            ["CM-11"]),
        new(
            "POL-INC",
            "Incident response policy",
            "A person who sees a security incident reports it the same day. The response owner writes down what happened, who was told, and what was restored, and keeps that note with the control evidence.",
            ["IR-02"]),
        new(
            "POL-BCP",
            "Business continuity policy",
            "The company keeps a short list of services it must restore and a named owner for each one. A restore is practiced often enough that the owner can show the last result.",
            ["BC-01"]),
        new(
            "POL-PRI",
            "Personal data policy",
            "Personal data is collected for a stated purpose and kept only while that purpose still needs it. This example policy is mapped to the privacy control.",
            ["PP-01"]),
    ];
}

public static class PolicyWorkflow
{
    public static RuleResult Submit(PolicyState state) =>
        state == PolicyState.Draft ? RuleResult.Ok() : RuleResult.Fail("not_draft");

    public static RuleResult Approve(PolicyState state) =>
        state == PolicyState.InReview ? RuleResult.Ok() : RuleResult.Fail("not_in_review");
}

public static class AcknowledgementRules
{
    public static bool NeedsReminder(DateOnly dueOn, DateTimeOffset? acknowledgedAt, DateTimeOffset? remindedAt, DateOnly today)
    {
        if (acknowledgedAt is not null || dueOn >= today)
        {
            return false;
        }

        if (remindedAt is null)
        {
            return true;
        }

        return DateOnly.FromDateTime(remindedAt.Value.UtcDateTime) < today;
    }
}
