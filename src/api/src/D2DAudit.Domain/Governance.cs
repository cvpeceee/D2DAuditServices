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

    public string ReviewCadence { get; set; } = "Quarterly";

    public string AcceptedBy { get; set; } = "";

    public DateOnly? AcceptedOn { get; set; }

    public DateOnly? AcceptanceExpiresOn { get; set; }

    public decimal? SingleLossExpectancy { get; set; }

    public decimal? AnnualRateOfOccurrence { get; set; }

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

    public string BackgroundProvider { get; set; } = "";

    public string BackgroundLink { get; set; } = "";

    public Guid? BackgroundEvidenceId { get; set; }

    public bool BackgroundExcluded { get; set; }

    public string BackgroundExclusionReason { get; set; } = "";

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

public sealed record RiskLibraryEntry(
    string Code,
    string Title,
    string Statement,
    int Likelihood,
    int Impact,
    string Treatment,
    string Plan,
    IReadOnlyList<string>? SuggestedControls = null,
    string Category = "Security")
{
    public IReadOnlyList<string> Controls => SuggestedControls ?? [];
}

public static class RiskLibrary
{
    public static IReadOnlyList<RiskLibraryEntry> All { get; } =
    [
        new("R-01", "Public marketing site", "A public bucket can expose files that were not meant to be public.", 4, 5, "Mitigate", "Block public access and confirm the next collection.", ["CFG-01", "CR-02"], "Cloud"),
        new("R-03", "Leaver still has access", "A person who left can still reach a system.", 4, 5, "Mitigate", "Remove lingering accounts within one business day.", ["AC-07", "HR-06"], "Access"),
        new("LIB-01", "Key person dependency", "One person holds an operational step that nobody else can run.", 3, 4, "Mitigate", "Name a second person and write the step down.", ["GV-02", "BC-01"], "People"),
        new("LIB-02", "Restore test missed", "A backup exists, and the last restore practice is late.", 3, 4, "Mitigate", "Run the restore and keep the result with the control.", ["BC-04"], "Resilience"),
        new("LIB-03", "Subprocessor change", "A supplier starts processing customer data without a fresh review.", 3, 5, "Mitigate", "Review the supplier before the data moves.", ["GV-04"], "Suppliers"),
        new("LIB-04", "Shared admin login", "Several people sign in with one administrator account, so actions cannot be traced to a person.", 3, 4, "Mitigate", "Issue named admin accounts and retire the shared one.", ["AC-04", "LG-03"], "Access"),
        new("LIB-05", "Second factor not enforced", "A sign-in path accepts a password alone for a system holding customer data.", 4, 5, "Mitigate", "Turn on enforced multi-factor sign-in at the identity provider.", ["AC-12"], "Access"),
        new("LIB-06", "Access creep after role change", "People keep old permissions when they move teams.", 3, 3, "Mitigate", "Review access at every role change and in the quarterly review.", ["AC-15", "AC-07"], "Access"),
        new("LIB-07", "Production data in test", "Copies of live customer records end up in lower environments.", 3, 4, "Mitigate", "Mask or synthesize data before it leaves production.", ["CM-11", "CR-05"], "Data"),
        new("LIB-08", "Unencrypted storage volume", "A disk or database snapshot is stored without encryption at rest.", 2, 4, "Mitigate", "Enforce default encryption through cloud policy.", ["CR-02", "CFG-01"], "Cloud"),
        new("LIB-09", "Signing key leaked in code", "A credential or private key is committed to a source repository.", 3, 5, "Mitigate", "Scan commits for secrets and rotate anything found.", ["CR-05", "CM-14"], "Engineering"),
        new("LIB-10", "Unreviewed code reaches production", "A change ships without a second person looking at it.", 3, 4, "Mitigate", "Require pull request approval before merge to the main branch.", ["CM-11", "CM-14"], "Engineering"),
        new("LIB-11", "Critical patch delayed", "A known exploited flaw stays open past the agreed fix window.", 3, 5, "Mitigate", "Track patch age and escalate items past their window.", ["VM-02", "VM-05"], "Vulnerability"),
        new("LIB-12", "Laptop without disk encryption", "A company laptop is lost while its drive is readable.", 3, 4, "Mitigate", "Enforce full-disk encryption through device management.", ["EP-03", "EP-08"], "Endpoint"),
        new("LIB-13", "Malware on a workstation", "A device runs without active malware protection.", 3, 4, "Mitigate", "Deploy endpoint protection and alert on gaps.", ["MAL-01", "EP-08"], "Endpoint"),
        new("LIB-14", "Logs missing during an incident", "The records needed to investigate an event were never kept or were deleted early.", 3, 4, "Mitigate", "Centralize logs and set retention to the policy period.", ["LG-03", "LG-08"], "Monitoring"),
        new("LIB-15", "Alert fatigue", "Real alerts are lost among noisy ones and nobody responds in time.", 3, 3, "Mitigate", "Tune alert thresholds and name an on-call owner.", ["LG-08", "IR-02"], "Monitoring"),
        new("LIB-16", "Incident response untested", "The response plan has not been exercised this year.", 3, 4, "Mitigate", "Run a tabletop exercise and record the lessons.", ["IR-02"], "Incident"),
        new("LIB-17", "Breach notice deadline missed", "A reportable incident is not disclosed within the regulatory window.", 2, 5, "Mitigate", "Add notification timers and legal contacts to the response plan.", ["IR-02", "PP-01"], "Privacy"),
        new("LIB-18", "Single region outage", "The service runs in one cloud region with no failover path.", 2, 5, "Mitigate", "Document recovery to a second region and test it.", ["BC-01", "BC-04"], "Resilience"),
        new("LIB-19", "Ransomware reaches backups", "An attacker can encrypt or delete backup copies along with production.", 2, 5, "Mitigate", "Keep immutable or offline backup copies.", ["BC-04", "MAL-01"], "Resilience"),
        new("LIB-20", "Cloud cost runaway", "A misconfigured workload consumes budget fast enough to threaten operations.", 2, 3, "Mitigate", "Set budget alerts and spending caps per account.", ["CFG-01"], "Cloud"),
        new("LIB-21", "Open network port", "A management port is reachable from the internet.", 3, 5, "Mitigate", "Restrict inbound rules and review security groups monthly.", ["CFG-01", "VM-05"], "Cloud"),
        new("LIB-22", "Excessive cloud permissions", "Service identities hold wildcard permissions they never use.", 3, 4, "Mitigate", "Apply least privilege and remove unused grants.", ["AC-04", "AC-15"], "Cloud"),
        new("LIB-23", "Supplier security unknown", "A critical supplier has no recent security assurance on file.", 3, 4, "Mitigate", "Collect a report or questionnaire before renewal.", ["GV-04"], "Suppliers"),
        new("LIB-24", "Supplier outage", "A key supplier becomes unavailable and there is no fallback.", 2, 4, "Transfer", "Agree service credits and identify a fallback supplier.", ["GV-04", "BC-01"], "Suppliers"),
        new("LIB-25", "Phishing compromise", "An employee enters credentials on a fake sign-in page.", 4, 4, "Mitigate", "Run awareness training and phishing simulations.", ["HR-08", "AC-12"], "People"),
        new("LIB-26", "Background checks skipped", "Someone with access to sensitive data starts before screening completes.", 2, 4, "Mitigate", "Block account creation until screening clears.", ["HR-01"], "People"),
        new("LIB-27", "Policies not acknowledged", "Staff have not confirmed they read the current security policies.", 3, 2, "Mitigate", "Send acknowledgement reminders and track completion.", ["GV-01", "HR-08"], "Governance"),
        new("LIB-28", "Policy out of date", "A policy has not been reviewed within its review cycle.", 3, 3, "Mitigate", "Assign owners and calendar the annual review.", ["GV-01", "GV-03"], "Governance"),
        new("LIB-29", "Risk register neglected", "Risks are recorded once and never reassessed.", 3, 3, "Mitigate", "Set a review cadence for every risk and report overdue items.", ["RM-01", "GV-02"], "Governance"),
        new("LIB-30", "Personal data kept too long", "Customer records stay beyond the stated retention period.", 3, 4, "Mitigate", "Automate deletion against the retention schedule.", ["PP-01", "CR-05"], "Privacy"),
        new("LIB-31", "Data subject request missed", "A request to access or delete personal data is not answered on time.", 2, 4, "Mitigate", "Route requests to a tracked queue with due dates.", ["PP-01"], "Privacy"),
        new("LIB-32", "Cross-border transfer without basis", "Personal data moves to another jurisdiction without a lawful transfer mechanism.", 2, 5, "Mitigate", "Map transfers and sign the needed contractual clauses.", ["PP-01", "GV-04"], "Privacy"),
        new("LIB-33", "Payment card data stored", "Card numbers are captured in logs or tickets outside the payment flow.", 2, 5, "Avoid", "Tokenize at the edge and scrub logs for card patterns.", ["CR-02", "LG-03"], "Payments"),
        new("LIB-34", "Office access uncontrolled", "Visitors can reach areas with equipment or paper records unescorted.", 2, 3, "Mitigate", "Badge access and a visitor log at reception.", ["PH-02"], "Physical"),
        new("LIB-35", "Model output harms a customer", "An AI feature produces incorrect or biased output that a customer relies on.", 3, 4, "Mitigate", "Add human review for high-impact outputs and monitor quality.", ["GV-03", "RM-01"], "AI"),
        new("LIB-36", "Training data provenance unclear", "A model is trained on data whose source or license is unknown.", 3, 4, "Mitigate", "Record dataset lineage and approvals before training.", ["GV-03", "PP-01"], "AI"),
        new("LIB-37", "Prompt injection", "Untrusted input steers an AI assistant into leaking data or taking actions.", 3, 4, "Mitigate", "Isolate tools, filter inputs and test adversarial prompts.", ["VM-05", "CM-14"], "AI"),
        new("LIB-38", "Plant network reachable from office", "Operational technology shares a flat network with corporate devices.", 2, 5, "Mitigate", "Segment the plant network and control the conduits.", ["CFG-01", "AC-04"], "Operational technology"),
        new("LIB-39", "Insider data download", "A trusted user exports large volumes of customer data before leaving.", 2, 5, "Mitigate", "Alert on bulk exports and review leaver activity.", ["LG-08", "HR-06"], "People"),
        new("LIB-40", "Cyber insurance gap", "A serious incident would cost more than current insurance covers.", 2, 4, "Transfer", "Review coverage limits against quantified loss estimates.", ["RM-01", "GV-02"], "Governance"),
    ];
}

public static class RiskQuantification
{
    public static decimal? AnnualizedLossExpectancy(decimal? singleLossExpectancy, decimal? annualRateOfOccurrence) =>
        singleLossExpectancy is decimal sle && annualRateOfOccurrence is decimal aro && sle >= 0 && aro >= 0
            ? decimal.Round(sle * aro, 2)
            : null;
}

public static class RiskCadence
{
    public static IReadOnlyList<string> All { get; } = ["Monthly", "Quarterly", "SemiAnnual", "Annual"];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);

    public static DateOnly NextReview(DateOnly from, string cadence) => cadence switch
    {
        "Monthly" => from.AddMonths(1),
        "SemiAnnual" => from.AddMonths(6),
        "Annual" => from.AddYears(1),
        _ => from.AddMonths(3),
    };
}

public sealed record HeatmapCell(int Likelihood, int Impact, int Score, string Band, int Count, IReadOnlyList<string> RiskCodes);

public sealed record RiskHeatmapResult(IReadOnlyList<HeatmapCell> Inherent, IReadOnlyList<HeatmapCell> Residual, int Total, int InherentCritical, int ResidualCritical);

public static class RiskHeatmap
{
    public static RiskHeatmapResult Build(IEnumerable<(string Code, int Likelihood, int Impact, int ResidualLikelihood, int ResidualImpact)> risks)
    {
        var list = risks.ToArray();
        var inherent = Grid(list.Select(item => (item.Code, item.Likelihood, item.Impact)));
        var residual = Grid(list.Select(item => (item.Code, item.ResidualLikelihood, item.ResidualImpact)));
        return new RiskHeatmapResult(
            inherent,
            residual,
            list.Length,
            inherent.Where(cell => cell.Band == "critical").Sum(cell => cell.Count),
            residual.Where(cell => cell.Band == "critical").Sum(cell => cell.Count));
    }

    private static HeatmapCell[] Grid(IEnumerable<(string Code, int Likelihood, int Impact)> points)
    {
        var clamped = points
            .Select(item => (item.Code, L: Math.Clamp(item.Likelihood, RiskScoring.ScaleMin, RiskScoring.ScaleMax), I: Math.Clamp(item.Impact, RiskScoring.ScaleMin, RiskScoring.ScaleMax)))
            .ToArray();
        var cells = new List<HeatmapCell>(25);
        for (var likelihood = RiskScoring.ScaleMax; likelihood >= RiskScoring.ScaleMin; likelihood--)
        {
            for (var impact = RiskScoring.ScaleMin; impact <= RiskScoring.ScaleMax; impact++)
            {
                var codes = clamped.Where(item => item.L == likelihood && item.I == impact).Select(item => item.Code).Order(StringComparer.Ordinal).ToArray();
                var score = RiskScoring.Score(likelihood, impact);
                cells.Add(new HeatmapCell(likelihood, impact, score, RiskScoring.Band(score), codes.Length, codes));
            }
        }

        return cells.ToArray();
    }
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
