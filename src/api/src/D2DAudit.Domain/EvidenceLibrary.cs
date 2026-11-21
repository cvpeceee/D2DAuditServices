namespace D2DAudit.Domain;

public enum EvidenceCadence
{
    OneTime,
    Monthly,
    Quarterly,
    Semiannual,
    Annual,
}

public sealed class EvidenceDocument
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public EvidenceCadence Cadence { get; set; } = EvidenceCadence.Annual;

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public Guid? CurrentEvidenceId { get; set; }

    public string? CurrentSha256 { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<EvidenceDocumentLink> Links { get; set; } = [];

    public List<EvidenceRequest> Requests { get; set; } = [];
}

public sealed class EvidenceDocumentLink
{
    public Guid DocumentId { get; set; }

    public EvidenceDocument? Document { get; set; }

    public string ControlCode { get; set; } = "";
}

public sealed class EvidenceRequest
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public EvidenceDocument? Document { get; set; }

    public string AssigneeName { get; set; } = "";

    public string AssigneeEmail { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public string Note { get; set; } = "";

    public string Status { get; set; } = EvidenceRequestRules.Open;

    public string RequestedByName { get; set; } = "";

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? FulfilledAt { get; set; }
}

public static class EvidenceExpiry
{
    public const string Missing = "missing";
    public const string Current = "current";
    public const string ExpiringSoon = "expiringSoon";
    public const string Expired = "expired";
    public const int WarningDays = 30;

    public static DateOnly? NextValidUntil(DateOnly from, EvidenceCadence cadence) => cadence switch
    {
        EvidenceCadence.Monthly => from.AddMonths(1),
        EvidenceCadence.Quarterly => from.AddMonths(3),
        EvidenceCadence.Semiannual => from.AddMonths(6),
        EvidenceCadence.Annual => from.AddYears(1),
        _ => null,
    };

    public static string Status(bool hasEvidence, DateOnly? validUntil, DateOnly today, int warningDays = WarningDays)
    {
        if (!hasEvidence)
        {
            return Missing;
        }

        if (validUntil is null)
        {
            return Current;
        }

        if (validUntil.Value < today)
        {
            return Expired;
        }

        return validUntil.Value <= today.AddDays(warningDays) ? ExpiringSoon : Current;
    }

    public static bool NeedsRenewal(string status) => status is Expired or ExpiringSoon or Missing;

    /// <summary>A control backed by library evidence is ready only while at least one linked item is still valid.</summary>
    public static bool LibraryBlocksReadiness(IReadOnlyCollection<string> linkedStatuses) =>
        linkedStatuses.Count > 0 && !linkedStatuses.Any(status => status is Current or ExpiringSoon);
}

public static class EvidenceRequestRules
{
    public const string Open = "Open";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";

    public static RuleResult Validate(string? name, string? email, DateOnly dueOn, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return RuleResult.Fail("invalid_assignee");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Trim().Length > 320)
        {
            return RuleResult.Fail("invalid_assignee");
        }

        return dueOn < today ? RuleResult.Fail("invalid_due") : RuleResult.Ok();
    }

    public static bool IsOverdue(string status, DateOnly dueOn, DateOnly today) => status == Open && dueOn < today;
}

public sealed class ControlProfile
{
    public string ControlCode { get; set; } = "";

    public string ImplementationGuidance { get; set; } = "";

    public string EvidenceRequirement { get; set; } = "";

    public int RequiredEvidenceCount { get; set; } = 1;

    public bool RequiresPassingTest { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ControlComment
{
    public Guid Id { get; set; }

    public string ControlCode { get; set; } = "";

    public string AuthorName { get; set; } = "";

    public string AuthorEmail { get; set; } = "";

    public string Body { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public List<ControlCommentRevision> Revisions { get; set; } = [];
}

public sealed class ControlCommentRevision
{
    public Guid Id { get; set; }

    public Guid CommentId { get; set; }

    public ControlComment? Comment { get; set; }

    public string Body { get; set; } = "";

    public DateTimeOffset ReplacedAt { get; set; }
}

public sealed record ReadinessInput(
    string? OwnerEmail,
    IReadOnlyList<TestOutcome> TestOutcomes,
    int EvidenceCount,
    int RequiredEvidenceCount,
    bool RequiresPassingTest,
    IReadOnlyList<string> LibraryStatuses);

public sealed record ReadinessCheck(string Key, bool Met, string Detail);

public sealed record ReadinessReport(bool Ready, int Percent, IReadOnlyList<ReadinessCheck> Checks);

public static class ControlReadiness
{
    public static ReadinessReport Evaluate(ReadinessInput input)
    {
        var checks = new List<ReadinessCheck>
        {
            new("owner", !string.IsNullOrWhiteSpace(input.OwnerEmail), string.IsNullOrWhiteSpace(input.OwnerEmail) ? "Assign an owner." : "Owner assigned."),
        };

        var ran = input.TestOutcomes.Where(item => item != TestOutcome.NotRun).ToArray();
        var failing = ran.Count(item => item == TestOutcome.Fail);
        var passing = ran.Count(item => item == TestOutcome.Pass);
        if (failing > 0)
        {
            checks.Add(new("tests", false, $"{failing} test(s) failing."));
        }
        else if (input.RequiresPassingTest && passing == 0)
        {
            checks.Add(new("tests", false, "A passing test is required."));
        }
        else
        {
            checks.Add(new("tests", true, ran.Length == 0 ? "No tests have run." : $"{passing} test(s) passing."));
        }

        var required = Math.Max(0, input.RequiredEvidenceCount);
        checks.Add(input.EvidenceCount >= required
            ? new("evidence", true, $"{input.EvidenceCount} evidence item(s) on file.")
            : new("evidence", false, $"{required - input.EvidenceCount} more evidence item(s) needed."));

        if (input.LibraryStatuses.Count > 0)
        {
            var blocked = EvidenceExpiry.LibraryBlocksReadiness(input.LibraryStatuses);
            var expired = input.LibraryStatuses.Count(item => item is EvidenceExpiry.Expired or EvidenceExpiry.Missing);
            var detail = expired > 0 ? $"{expired} linked item(s) expired; at least one is current." : "Linked library evidence is current.";
            checks.Add(new("library", !blocked, blocked ? "All linked library evidence is expired or missing." : detail));
        }

        var met = checks.Count(item => item.Met);
        return new ReadinessReport(met == checks.Count, checks.Count == 0 ? 100 : met * 100 / checks.Count, checks);
    }

    /// <summary>Downgrades a passing control whose library evidence has all lapsed.</summary>
    public static ControlStatus ApplyLibrary(ControlStatus current, IReadOnlyCollection<string> linkedStatuses) =>
        current == ControlStatus.Passing && EvidenceExpiry.LibraryBlocksReadiness(linkedStatuses) ? ControlStatus.NeedsEvidence : current;

    public static RuleResult ValidateComment(string? body) =>
        string.IsNullOrWhiteSpace(body) || body.Trim().Length > 2000 ? RuleResult.Fail("invalid_comment") : RuleResult.Ok();
}
