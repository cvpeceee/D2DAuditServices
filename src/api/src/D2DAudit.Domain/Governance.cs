namespace D2DAudit.Domain;

public enum RiskTreatment
{
    Accept,
    Mitigate,
    Transfer,
    Avoid,
}

public enum PersonState
{
    Active,
    Offboarding,
    Offboarded,
}

public enum DirectorySource
{
    Manual,
    Google,
    Microsoft,
    Mixed,
}

public enum TrainingState
{
    NotAssigned,
    Assigned,
    Complete,
    Overdue,
}

public enum ChecklistKind
{
    Onboarding,
    Offboarding,
}

public enum SoaImplementation
{
    NotStarted,
    InProgress,
    Implemented,
    NotApplicable,
}

public enum VendorCriticality
{
    Low,
    Medium,
    High,
    Critical,
}

public enum VendorReviewState
{
    NotStarted,
    InReview,
    Approved,
    Expired,
}

public enum AccessDecision
{
    Pending,
    Approved,
    Revoked,
}

public enum CampaignState
{
    Draft,
    Open,
    Remediation,
    Completed,
}

public static class RiskScoring
{
    public const int ScaleMin = 1;

    public const int ScaleMax = 5;

    public static bool OnScale(int value) => value is >= ScaleMin and <= ScaleMax;

    public static int Score(int likelihood, int impact) => likelihood * impact;

    public static string Band(int score) => score switch
    {
        >= 15 => "critical",
        >= 8 => "high",
        >= 4 => "medium",
        _ => "low",
    };
}

public static class LeaverAccess
{
    public static bool Flagged(PersonState state, IEnumerable<bool> accountActive) =>
        state == PersonState.Offboarded && accountActive.Any(active => active);
}

public static class AccessCampaignRules
{
    public static RuleResult Complete(IReadOnlyCollection<AccessDecision> decisions)
    {
        if (decisions.Count == 0)
        {
            return RuleResult.Fail("empty_campaign");
        }

        return decisions.All(item => item is AccessDecision.Approved or AccessDecision.Revoked)
            ? RuleResult.Ok()
            : RuleResult.Fail("decisions_pending");
    }
}

public static class ChecklistTasks
{
    public static IReadOnlyList<string> Onboarding() => ["account", "mfa", "policies", "training", "equipment"];

    public static IReadOnlyList<string> Offboarding() => ["disable", "assets", "access", "interview"];
}

public static class AnnexCodes
{
    public static bool IsControl(string code)
    {
        var parts = code.Split('.');
        return parts.Length == 3
            && parts[0] == "A"
            && int.TryParse(parts[1], out _)
            && int.TryParse(parts[2], out _);
    }
}

public sealed record DirectoryAccount(string Provider, string ExternalId, string Name, string Email, bool Offboarded, bool Active);

public sealed record SyncedPerson(string Email, string Name, PersonState State, IReadOnlyList<DirectoryAccount> Accounts);

public static class DirectorySync
{
    public static IReadOnlyList<SyncedPerson> People(IEnumerable<DirectoryAccount> accounts) =>
        accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Email))
            .GroupBy(account => account.Email.Trim().ToLowerInvariant())
            .Select(group =>
            {
                var name = group.Select(account => account.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key;
                var state = group.Any(account => account.Offboarded) ? PersonState.Offboarded : PersonState.Active;
                return new SyncedPerson(group.Key, name, state, group.ToArray());
            })
            .OrderBy(person => person.Name, StringComparer.Ordinal)
            .ToArray();

    public static DirectorySource SourceOf(IReadOnlyList<DirectoryAccount> accounts)
    {
        var providers = accounts.Select(account => account.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (providers.Length == 1 && providers[0].Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            return DirectorySource.Google;
        }

        if (providers.Length == 1 && providers[0].Equals("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return DirectorySource.Microsoft;
        }

        return providers.Length == 0 ? DirectorySource.Manual : DirectorySource.Mixed;
    }
}

public sealed class RiskItem
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Statement { get; set; } = "";

    public int Likelihood { get; set; }

    public int Impact { get; set; }

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public RiskTreatment Treatment { get; set; }

    public string TreatmentPlan { get; set; } = "";

    public DateOnly ReviewOn { get; set; }

    public int ResidualLikelihood { get; set; }

    public int ResidualImpact { get; set; }

    public bool Approved { get; set; }

    public string ApprovedBy { get; set; } = "";

    public DateOnly? ApprovedOn { get; set; }

    public string LibraryCode { get; set; } = "";

    public List<RiskControlLink> Controls { get; set; } = [];
}

public sealed class RiskControlLink
{
    public Guid RiskId { get; set; }

    public RiskItem? Risk { get; set; }

    public string ControlCode { get; set; } = "";
}

public sealed class DirectoryPerson
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    public string Department { get; set; } = "";

    public string Role { get; set; } = "";

    public DateOnly? HireOn { get; set; }

    public DateOnly? LeftOn { get; set; }

    public DirectorySource Source { get; set; }

    public PersonState State { get; set; }

    public TrainingState Training { get; set; }

    public DateOnly? TrainingDueOn { get; set; }

    public DateOnly? TrainingCompletedOn { get; set; }

    public bool MfaEnrolled { get; set; }

    public string BackgroundStatus { get; set; } = "NotStarted";

    public DateOnly? BackgroundOn { get; set; }

    public string DeviceStatus { get; set; } = "Missing";

    public string StatusReason { get; set; } = "";

    public List<PersonTask> Tasks { get; set; } = [];

    public List<PersonAccount> Accounts { get; set; } = [];
}

public sealed class PersonTask
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public DirectoryPerson? Person { get; set; }

    public ChecklistKind Kind { get; set; }

    public string TaskCode { get; set; } = "";

    public bool Done { get; set; }
}

public sealed class PersonAccount
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public DirectoryPerson? Person { get; set; }

    public string Provider { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string AccountName { get; set; } = "";

    public bool Active { get; set; }
}

public sealed class SoaEntry
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public bool Applicable { get; set; }

    public string Justification { get; set; } = "";

    public SoaImplementation Implementation { get; set; }
}

public sealed class VendorRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Service { get; set; } = "";

    public VendorCriticality Criticality { get; set; }

    public string DataAccess { get; set; } = "";

    public VendorReviewState ReviewState { get; set; }

    public DateOnly ReviewDueOn { get; set; }

    public int RiskRating { get; set; }

    public string Decision { get; set; } = "Pending";

    public int ResidualLikelihood { get; set; } = 2;

    public int ResidualImpact { get; set; } = 2;

    public List<VendorDocument> Documents { get; set; } = [];

    public List<VendorQuestion> Questions { get; set; } = [];
}

public sealed class VendorDocument
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public VendorRecord? Vendor { get; set; }

    public string Title { get; set; } = "";

    public string Kind { get; set; } = "";

    public DateOnly? IssuedOn { get; set; }
}

public sealed class VendorQuestion
{
    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public VendorRecord? Vendor { get; set; }

    public string Prompt { get; set; } = "";

    public string Answer { get; set; } = "";
}

public sealed class AccessCampaign
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public CampaignState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? EvidenceId { get; set; }

    public string Providers { get; set; } = "";

    public List<AccessReviewItem> Items { get; set; } = [];
}

public sealed class AccessReviewItem
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public AccessCampaign? Campaign { get; set; }

    public string PersonName { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public string Provider { get; set; } = "";

    public string AccountName { get; set; } = "";

    public AccessDecision Decision { get; set; }

    public string? ReviewerName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public bool NeedsRemediation { get; set; }

    public string RemediationNote { get; set; } = "";
}

public static class TrainingRules
{
    public static bool WithinTwelveMonths(DateOnly? completedOn, DateOnly today) =>
        completedOn is DateOnly completed && completed.AddMonths(12) >= today;

    public static bool OrganizationCurrent(IEnumerable<(string State, DateOnly? CompletedOn)> people, DateOnly today) =>
        people.Where(person => person.State == "Active").All(person => WithinTwelveMonths(person.CompletedOn, today));
}

public sealed record RiskLibraryEntry(string Code, string Title, string Statement, int Likelihood, int Impact, string Treatment, string Plan);

public static class RiskLibrary
{
    public static IReadOnlyList<RiskLibraryEntry> All { get; } =
    [
        new("R-01", "Public marketing site", "A public bucket can expose files that were not meant to be public.", 4, 5, "Mitigate", "Block public access and confirm the next collection."),
        new("R-03", "Leaver still has access", "A person who left can still reach a system.", 4, 5, "Mitigate", "Remove lingering accounts within one business day."),
        new("LIB-01", "Key person dependency", "One person holds an operational step that nobody else can run.", 3, 4, "Mitigate", "Name a second person and write the step down."),
        new("LIB-02", "Restore test missed", "A backup exists, and the last restore practice is late.", 3, 4, "Mitigate", "Run the restore and keep the result with the control."),
        new("LIB-03", "Subprocessor change", "A supplier starts processing customer data without a fresh review.", 3, 5, "Mitigate", "Review the supplier before the data moves."),
    ];
}

public sealed class TrackedAsset
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string AssetType { get; set; } = "";

    public string Source { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public string Environment { get; set; } = "";

    public string Classification { get; set; } = "";

    public bool InScope { get; set; } = true;

    public DateTimeOffset LastSeen { get; set; }

    public string ExternalId { get; set; } = "";
}

public sealed class WorkTask
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string LinkedKind { get; set; } = "";

    public string LinkedCode { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public string Status { get; set; } = "Open";

    public string Recurrence { get; set; } = "None";
}

public sealed class TrainingModule
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string ModuleType { get; set; } = "BuiltIn";

    public string AssignedGroup { get; set; } = "Everyone";

    public DateOnly DueOn { get; set; }

    public string? ExternalUrl { get; set; }
}

public sealed class TrainingCompletion
{
    public Guid Id { get; set; }

    public Guid ModuleId { get; set; }

    public TrainingModule? Module { get; set; }

    public string PersonEmail { get; set; } = "";

    public DateTimeOffset CompletedAt { get; set; }

    public int Score { get; set; }

    public Guid? EvidenceId { get; set; }
}

public static class TrainingContent
{
    public const string AnnualCode = "SEC-ANNUAL";

    public const string LmsCode = "SEC-LMS";

    public static IReadOnlyList<TrainingSlide> Slides { get; } =
    [
        new("Why this matters", "Customer data stays in the approved warehouse. A personal drive, a chat thread, and a public bucket are not a warehouse."),
        new("What you do", "Use the company account, keep a second factor on, and finish this module once a year."),
        new("How you report", "Tell Security the same day you see a phishing message, a lost laptop, or a file in the wrong place."),
    ];

    public static IReadOnlyList<TrainingPrompt> Questions { get; } =
    [
        new("Who do you tell about a suspected phishing message?", ["Security", "A customer", "Nobody"], 0),
        new("When do you finish this module?", ["Every year", "Never", "Only after an incident"], 0),
        new("Where do customer files belong?", ["The approved warehouse", "A personal drive", "Email"], 0),
    ];

    public static int Score(IReadOnlyList<int> answers)
    {
        var score = 0;
        for (var index = 0; index < Questions.Count; index++)
        {
            if (index < answers.Count && answers[index] == Questions[index].Answer)
            {
                score++;
            }
        }

        return score;
    }

    public static bool Passed(int score) => score == Questions.Count;
}

public sealed record TrainingSlide(string Title, string Body);

public sealed record TrainingPrompt(string Prompt, IReadOnlyList<string> Choices, int Answer);
