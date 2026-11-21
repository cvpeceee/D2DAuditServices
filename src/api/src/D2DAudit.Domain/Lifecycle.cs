namespace D2DAudit.Domain;

public enum LifecycleKind
{
    Onboarding,
    Offboarding,
}

public sealed class EmployeeGroup
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Departments { get; set; } = "";

    public string RoleKeywords { get; set; } = "";

    public string PolicyCodes { get; set; } = "";

    public string TrainingCodes { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LifecycleTemplateStep
{
    public Guid Id { get; set; }

    public LifecycleKind Kind { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Category { get; set; } = "";

    public int SlaHours { get; set; }

    public int Order { get; set; }

    public string GroupCode { get; set; } = "";

    public bool RevokesAccess { get; set; }

    public bool PerAccount { get; set; }
}

public sealed class LifecycleSettings
{
    public Guid Id { get; set; }

    public int OffboardingAccessSlaHours { get; set; } = LifecycleRules.DefaultAccessSlaHours;

    public int OffboardingCompleteSlaHours { get; set; } = LifecycleRules.DefaultCompleteSlaHours;

    public int OnboardingDays { get; set; } = LifecycleRules.DefaultOnboardingDays;

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LifecycleChecklist
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public string PersonName { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public LifecycleKind Kind { get; set; }

    public string Trigger { get; set; } = "Manual";

    public string Reason { get; set; } = "";

    public DateOnly EffectiveOn { get; set; }

    public DateTimeOffset TriggeredAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? AccessDueAt { get; set; }

    public DateTimeOffset? AccessRevokedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string State { get; set; } = "Open";

    public string StartedBy { get; set; } = "";

    public List<LifecycleStep> Steps { get; set; } = [];
}

public sealed class LifecycleStep
{
    public Guid Id { get; set; }

    public Guid ChecklistId { get; set; }

    public LifecycleChecklist? Checklist { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Category { get; set; } = "";

    public string Provider { get; set; } = "";

    public string AccountName { get; set; } = "";

    public bool RevokesAccess { get; set; }

    public int Order { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public bool Done { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string CompletedBy { get; set; } = "";

    public string Result { get; set; } = "";

    public Guid? EvidenceId { get; set; }
}

public sealed record LifecycleTemplateSeed(LifecycleKind Kind, string Code, string Title, string Category, int SlaHours, bool RevokesAccess, bool PerAccount, string GroupCode = "");

public sealed record EmployeeGroupSeed(string Code, string Name, string Description, string Departments, string RoleKeywords, string PolicyCodes, string TrainingCodes);

public sealed record LifecycleAccount(string Provider, string AccountName, bool Active);

public sealed record GeneratedStep(string Code, string Title, string Category, string Provider, string AccountName, bool RevokesAccess, int Order, DateTimeOffset DueAt);

public sealed record ChecklistSlaSnapshot(string PersonName, LifecycleKind Kind, DateTimeOffset TriggeredAt, DateTimeOffset DueAt, DateTimeOffset? AccessDueAt, DateTimeOffset? AccessRevokedAt, DateTimeOffset? CompletedAt);

public static class LifecycleRules
{
    public const int DefaultAccessSlaHours = 24;

    public const int DefaultCompleteSlaHours = 72;

    public const int DefaultOnboardingDays = 14;

    public const string OffboardingTestName = "Terminated personnel access removed within SLA";

    public const string OnboardingTestName = "New hires complete onboarding within N days";

    public const string OffboardingControl = "HR-08";

    public const string OnboardingControl = "HR-01";

    public static readonly IReadOnlyList<LifecycleTemplateSeed> DefaultTemplates =
    [
        new(LifecycleKind.Onboarding, "accept-policies", "Accept the security policies", "Policy", 72, false, false),
        new(LifecycleKind.Onboarding, "complete-training", "Complete security awareness training", "Training", 168, false, false),
        new(LifecycleKind.Onboarding, "enroll-device", "Enroll the work computer in device management", "Device", 72, false, false),
        new(LifecycleKind.Onboarding, "enable-mfa", "Turn on multi-factor sign-in", "Identity", 24, false, false),
        new(LifecycleKind.Onboarding, "background-check", "Background check cleared", "Screening", 168, false, false),
        new(LifecycleKind.Onboarding, "secure-coding", "Complete secure coding training", "Training", 240, false, false, "engineering"),
        new(LifecycleKind.Onboarding, "contractor-nda", "Sign the contractor confidentiality agreement", "Policy", 24, false, false, "contractors"),
        new(LifecycleKind.Offboarding, "revoke-sso", "Revoke single sign-on", "Access", 0, true, false),
        new(LifecycleKind.Offboarding, "revoke-app", "Revoke connected app account", "Access", 0, true, true),
        new(LifecycleKind.Offboarding, "remove-github", "Remove from the GitHub organization", "Access", 0, true, false, "engineering"),
        new(LifecycleKind.Offboarding, "recover-device", "Recover the work computer", "Device", 72, false, false),
        new(LifecycleKind.Offboarding, "transfer-ownership", "Transfer document and system ownership", "Ownership", 72, false, false),
    ];

    public static readonly IReadOnlyList<EmployeeGroupSeed> DefaultGroups =
    [
        new("everyone", "Everyone", "All active personnel", "*", "", "POL-ISP,POL-ACC", "SEC-ANNUAL"),
        new("engineering", "Engineering", "People who ship code or run production", "Engineering,Security", "engineer,developer,sre", "POL-ISP,POL-ACC,POL-CHG,POL-INC", "SEC-ANNUAL,SEC-CODE"),
        new("contractors", "Contractors", "Non-employee personnel with workspace access", "", "contractor,consultant", "POL-ISP,POL-ACC,POL-PRI", "SEC-ANNUAL"),
    ];

    public static IReadOnlyList<string> Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool InGroup(string departments, string roleKeywords, string department, string role)
    {
        var deps = Split(departments);
        if (deps.Contains("*"))
        {
            return true;
        }

        if (deps.Any(item => string.Equals(item, department.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Split(roleKeywords).Any(keyword => role.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> GroupsFor(IEnumerable<EmployeeGroupSeed> groups, string department, string role) =>
        groups.Where(group => InGroup(group.Departments, group.RoleKeywords, department, role))
            .Select(group => group.Code)
            .ToArray();

    public static DateTimeOffset AccessDue(DateTimeOffset triggeredAt, int accessSlaHours) =>
        triggeredAt.AddHours(Math.Clamp(accessSlaHours, 1, 24 * 30));

    public static IReadOnlyList<GeneratedStep> Generate(
        LifecycleKind kind,
        IEnumerable<LifecycleTemplateSeed> templates,
        IReadOnlyCollection<string> groupCodes,
        IEnumerable<LifecycleAccount> accounts,
        DateTimeOffset triggeredAt,
        int accessSlaHours,
        int completeSlaHours,
        int onboardingDays)
    {
        var steps = new List<GeneratedStep>();
        var order = 0;
        var accessDue = AccessDue(triggeredAt, accessSlaHours);
        var completeDue = CompleteDue(kind, triggeredAt, completeSlaHours, onboardingDays);
        var active = accounts.Where(account => account.Active).ToArray();
        foreach (var template in templates.Where(item => item.Kind == kind))
        {
            if (!string.IsNullOrWhiteSpace(template.GroupCode)
                && !groupCodes.Contains(template.GroupCode, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var due = completeDue;
            if (template.RevokesAccess)
            {
                due = accessDue;
            }
            else if (template.SlaHours > 0)
            {
                due = Min(triggeredAt.AddHours(template.SlaHours), completeDue);
            }

            if (template.PerAccount)
            {
                foreach (var account in active.OrderBy(item => item.Provider, StringComparer.Ordinal).ThenBy(item => item.AccountName, StringComparer.Ordinal))
                {
                    steps.Add(new GeneratedStep(
                        template.Code,
                        $"{template.Title}: {account.Provider} {account.AccountName}",
                        template.Category,
                        account.Provider,
                        account.AccountName,
                        template.RevokesAccess,
                        order++,
                        due));
                }

                continue;
            }

            steps.Add(new GeneratedStep(template.Code, template.Title, template.Category, "", "", template.RevokesAccess, order++, due));
        }

        return steps;
    }

    public static DateTimeOffset CompleteDue(LifecycleKind kind, DateTimeOffset triggeredAt, int completeSlaHours, int onboardingDays) =>
        kind == LifecycleKind.Offboarding
            ? triggeredAt.AddHours(Math.Clamp(completeSlaHours, 1, 24 * 60))
            : triggeredAt.AddDays(Math.Clamp(onboardingDays, 1, 120));

    public static string SlaState(DateTimeOffset dueAt, DateTimeOffset? doneAt, DateTimeOffset now)
    {
        if (doneAt is DateTimeOffset done)
        {
            return done <= dueAt ? "Met" : "Missed";
        }

        if (now > dueAt)
        {
            return "Overdue";
        }

        return dueAt - now <= TimeSpan.FromHours(12) ? "DueSoon" : "OnTrack";
    }

    public static CheckResult EvaluateOffboarding(IEnumerable<ChecklistSlaSnapshot> checklists, DateTimeOffset now, int accessSlaHours)
    {
        var rows = checklists.Where(item => item.Kind == LifecycleKind.Offboarding).ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, "No terminations need access removal.");
        }

        var missed = rows
            .Where(item =>
            {
                var due = item.AccessDueAt ?? AccessDue(item.TriggeredAt, accessSlaHours);
                return item.AccessRevokedAt is DateTimeOffset revoked ? revoked > due : now > due;
            })
            .Select(item => item.PersonName)
            .ToArray();
        if (missed.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, $"{rows.Length} terminations had access removed within {accessSlaHours} hours.");
        }

        return new CheckResult(TestOutcome.Fail, $"Access not removed within {accessSlaHours} hours for: {Names(missed)}.");
    }

    public static CheckResult EvaluateOnboarding(IEnumerable<ChecklistSlaSnapshot> checklists, DateTimeOffset now, int onboardingDays)
    {
        var rows = checklists.Where(item => item.Kind == LifecycleKind.Onboarding).ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, "No new hires are onboarding.");
        }

        var late = rows
            .Where(item => item.CompletedAt is DateTimeOffset done ? done > item.DueAt : now > item.DueAt)
            .Select(item => item.PersonName)
            .ToArray();
        if (late.Length == 0)
        {
            return new CheckResult(TestOutcome.Pass, $"{rows.Length} new hires completed or are on track to complete onboarding within {onboardingDays} days.");
        }

        return new CheckResult(TestOutcome.Fail, $"Onboarding not complete within {onboardingDays} days for: {Names(late)}.");
    }

    private static string Names(string[] names)
    {
        var shown = names.Take(12).ToArray();
        var extra = names.Length - shown.Length;
        return extra > 0 ? string.Join(", ", shown) + $" and {extra} more" : string.Join(", ", shown);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}
