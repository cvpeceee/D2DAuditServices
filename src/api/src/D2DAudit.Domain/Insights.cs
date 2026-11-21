using System.Globalization;

namespace D2DAudit.Domain;

/// <summary>One posture reading per tenant per day. The dashboard trend charts read these rows.</summary>
public sealed class PostureSnapshot
{
    public Guid Id { get; set; }

    public DateOnly Day { get; set; }

    public int ControlsPassing { get; set; }

    public int ControlsTotal { get; set; }

    public int FailingTests { get; set; }

    public int VulnCritical { get; set; }

    public int VulnHigh { get; set; }

    public int VulnMedium { get; set; }

    public int VulnLow { get; set; }

    public int OpenTasks { get; set; }

    public int OverdueItems { get; set; }

    /// <summary>JSON array of <see cref="FrameworkPosture"/>.</summary>
    public string FrameworksJson { get; set; } = "[]";

    /// <summary>JSON array of failing test ids, so the next run can tell which tests newly failed.</summary>
    public string FailingTestIdsJson { get; set; } = "[]";

    public DateTimeOffset RecordedAt { get; set; }
}

public sealed record FrameworkPosture(string Key, string Name, int Passing, int Total);

/// <summary>An in-app notification. A null recipient means every workspace member except auditors.</summary>
public sealed class UserNotification
{
    public Guid Id { get; set; }

    public string? RecipientEmail { get; set; }

    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Href { get; set; } = "";

    public string DedupeKey { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public sealed class NotificationReceipt
{
    public Guid Id { get; set; }

    public Guid NotificationId { get; set; }

    public string Email { get; set; } = "";

    public DateTimeOffset ReadAt { get; set; }
}

public sealed class NotificationPreference
{
    public Guid Id { get; set; }

    public string Email { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool InApp { get; set; } = true;

    public bool EmailDigest { get; set; } = true;

    public bool Slack { get; set; }
}

public sealed class ReportSchedule
{
    public Guid Id { get; set; }

    public string ReportKey { get; set; } = "";

    public string Frequency { get; set; } = "Weekly";

    public string Format { get; set; } = "pdf";

    public string Recipients { get; set; } = "";

    public bool Enabled { get; set; }

    public DateOnly NextRunOn { get; set; }

    public DateTimeOffset? LastSentAt { get; set; }

    public string? LastDeliveryId { get; set; }
}

public sealed record PostureControl(ControlStatus Status, IReadOnlyList<(string Key, string Name)> Frameworks);

public sealed record PostureInput(
    IReadOnlyList<PostureControl> Controls,
    IReadOnlyList<Guid> FailingTestIds,
    IReadOnlyList<string> OpenVulnerabilitySeverities,
    IReadOnlyList<DateOnly> OpenTaskDueDates,
    IReadOnlyList<DateOnly> OtherDueDates);

public static class PostureRules
{
    public static PostureSnapshot Compute(PostureInput input, DateOnly day, DateTimeOffset now)
    {
        var frameworks = input.Controls
            .SelectMany(control => control.Frameworks.Select(framework => (framework.Key, framework.Name, control.Status)))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => new FrameworkPosture(
                group.Key,
                group.First().Name,
                group.Count(item => item.Status == ControlStatus.Passing),
                group.Count()))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var severities = input.OpenVulnerabilitySeverities.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var overdue = input.OpenTaskDueDates.Count(due => due < day) + input.OtherDueDates.Count(due => due < day);
        return new PostureSnapshot
        {
            Id = Guid.NewGuid(),
            Day = day,
            ControlsPassing = input.Controls.Count(control => control.Status == ControlStatus.Passing),
            ControlsTotal = input.Controls.Count,
            FailingTests = input.FailingTestIds.Count,
            VulnCritical = severities.Count(value => value == "critical"),
            VulnHigh = severities.Count(value => value == "high"),
            VulnMedium = severities.Count(value => value == "medium"),
            VulnLow = severities.Count(value => value == "low"),
            OpenTasks = input.OpenTaskDueDates.Count,
            OverdueItems = overdue,
            FrameworksJson = System.Text.Json.JsonSerializer.Serialize(frameworks),
            FailingTestIdsJson = System.Text.Json.JsonSerializer.Serialize(input.FailingTestIds.Order().ToArray()),
            RecordedAt = now,
        };
    }

    public static IReadOnlyList<FrameworkPosture> Frameworks(PostureSnapshot snapshot) =>
        System.Text.Json.JsonSerializer.Deserialize<FrameworkPosture[]>(snapshot.FrameworksJson) ?? [];

    public static IReadOnlyList<Guid> FailingTests(PostureSnapshot? snapshot) =>
        snapshot is null ? [] : System.Text.Json.JsonSerializer.Deserialize<Guid[]>(snapshot.FailingTestIdsJson) ?? [];

    public static int Percent(int passing, int total) =>
        total == 0 ? 0 : (int)Math.Round(passing * 100d / total, MidpointRounding.AwayFromZero);
}

public static class NotificationKinds
{
    public const string TestFailing = "test_failing";
    public const string TaskAssigned = "task_assigned";
    public const string PolicyAck = "policy_ack";
    public const string ReviewDue = "review_due";
    public const string AuditorRequest = "auditor_request";
    public const string VendorOverdue = "vendor_overdue";

    public static readonly IReadOnlyList<string> All = [TestFailing, TaskAssigned, PolicyAck, ReviewDue, AuditorRequest, VendorOverdue];
}

public sealed record FailingTestFact(Guid TestId, string TestName, string ControlCode, string ControlOwnerEmail, string Detail);

public sealed record TaskFact(Guid Id, string Title, string OwnerEmail, DateOnly DueOn, string Status);

public sealed record AckFact(Guid VersionId, string PolicyCode, string PolicyTitle, string PersonEmail, DateOnly DueOn, bool Acknowledged);

public sealed record ReviewFact(string Kind, string Id, string Title, string OwnerEmail, DateOnly DueOn, string Href);

public sealed record RequestFact(Guid Id, string Code, string Title, DateOnly DueOn, bool Open);

public sealed record VendorFact(Guid Id, string Name, DateOnly ReviewDueOn);

public sealed record NotificationFacts(
    IReadOnlyList<FailingTestFact> NewlyFailingTests,
    IReadOnlyList<TaskFact> Tasks,
    IReadOnlyList<AckFact> Acknowledgements,
    IReadOnlyList<ReviewFact> Reviews,
    IReadOnlyList<RequestFact> Requests,
    IReadOnlyList<VendorFact> Vendors);

public sealed record NotificationDraft(string? RecipientEmail, string Kind, string Title, string Body, string Href, string DedupeKey);

public static class NotificationRules
{
    public const int ReviewWindowDays = 7;

    public static IReadOnlyList<NotificationDraft> Generate(NotificationFacts facts, DateOnly today)
    {
        var drafts = new List<NotificationDraft>();
        var stamp = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var test in facts.NewlyFailingTests)
        {
            drafts.Add(new NotificationDraft(
                Recipient(test.ControlOwnerEmail),
                NotificationKinds.TestFailing,
                Clip("Test started failing: " + test.TestName, 200),
                Clip(test.ControlCode + " — " + test.Detail, 1000),
                "/controls/" + test.ControlCode,
                NotificationKinds.TestFailing + ":" + test.TestId.ToString("N") + ":" + stamp));
        }

        foreach (var task in facts.Tasks.Where(task => task.Status == "Open" && !string.IsNullOrWhiteSpace(task.OwnerEmail)))
        {
            var email = Normalize(task.OwnerEmail);
            drafts.Add(new NotificationDraft(
                email,
                NotificationKinds.TaskAssigned,
                Clip("Task assigned to you: " + task.Title, 200),
                "Due " + task.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "/tasks",
                NotificationKinds.TaskAssigned + ":" + task.Id.ToString("N") + ":" + email));
        }

        foreach (var ack in facts.Acknowledgements.Where(ack => !ack.Acknowledged && !string.IsNullOrWhiteSpace(ack.PersonEmail)))
        {
            var email = Normalize(ack.PersonEmail);
            drafts.Add(new NotificationDraft(
                email,
                NotificationKinds.PolicyAck,
                Clip("Policy needs your acknowledgment: " + ack.PolicyTitle, 200),
                ack.PolicyCode + " is due " + ack.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "/me",
                NotificationKinds.PolicyAck + ":" + ack.VersionId.ToString("N") + ":" + email));
        }

        foreach (var review in facts.Reviews.Where(review => review.DueOn <= today.AddDays(ReviewWindowDays)))
        {
            var due = review.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            drafts.Add(new NotificationDraft(
                Recipient(review.OwnerEmail),
                NotificationKinds.ReviewDue,
                Clip((review.DueOn < today ? "Review overdue: " : "Review due: ") + review.Title, 200),
                review.Kind + " review due " + due,
                review.Href,
                NotificationKinds.ReviewDue + ":" + review.Kind + ":" + review.Id + ":" + due));
        }

        foreach (var request in facts.Requests.Where(request => request.Open))
        {
            drafts.Add(new NotificationDraft(
                null,
                NotificationKinds.AuditorRequest,
                Clip("Auditor request " + request.Code + ": " + request.Title, 200),
                "Due " + request.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "/auditor",
                NotificationKinds.AuditorRequest + ":" + request.Id.ToString("N")));
        }

        foreach (var vendor in facts.Vendors.Where(vendor => vendor.ReviewDueOn < today))
        {
            var due = vendor.ReviewDueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            drafts.Add(new NotificationDraft(
                null,
                NotificationKinds.VendorOverdue,
                Clip("Vendor review overdue: " + vendor.Name, 200),
                "The review was due " + due,
                "/vendors",
                NotificationKinds.VendorOverdue + ":" + vendor.Id.ToString("N") + ":" + due));
        }

        return drafts
            .GroupBy(draft => draft.DedupeKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>True when the reader can see the notification at all.</summary>
    public static bool Visible(string? recipientEmail, string readerEmail, bool readerIsAuditor) =>
        recipientEmail is null
            ? !readerIsAuditor
            : string.Equals(recipientEmail, Normalize(readerEmail), StringComparison.Ordinal);

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string? Recipient(string email) => string.IsNullOrWhiteSpace(email) ? null : Normalize(email);

    private static string Clip(string value, int length) => value.Length > length ? value[..length] : value;
}

public sealed record SearchEntry(string Kind, string Key, string Title, string Subtitle, string Href, IReadOnlyList<string> FrameworkKeys);

public static class SearchRules
{
    public static readonly IReadOnlyList<string> AuditorKinds = ["control", "policy", "evidence", "test", "framework"];

    public static readonly IReadOnlyList<string> EmployeeKinds = ["policy", "framework"];

    public const int Limit = 40;

    public static IReadOnlyList<SearchEntry> Filter(
        IEnumerable<SearchEntry> entries,
        string query,
        TenantRole role,
        IReadOnlyCollection<string> auditorFrameworkKeys)
    {
        var text = (query ?? "").Trim();
        if (text.Length == 0)
        {
            return [];
        }

        return entries
            .Where(entry => Allowed(entry, role, auditorFrameworkKeys))
            .Select(entry => (Entry: entry, Score: Score(entry, text)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Limit)
            .Select(item => item.Entry)
            .ToArray();
    }

    public static bool Allowed(SearchEntry entry, TenantRole role, IReadOnlyCollection<string> auditorFrameworkKeys) => role switch
    {
        TenantRole.Auditor => AuditorKinds.Contains(entry.Kind)
            && entry.FrameworkKeys.Any(key => auditorFrameworkKeys.Contains(key, StringComparer.Ordinal)),
        TenantRole.Employee => EmployeeKinds.Contains(entry.Kind),
        _ => true,
    };

    private static int Score(SearchEntry entry, string text)
    {
        if (entry.Key.Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (entry.Title.StartsWith(text, StringComparison.OrdinalIgnoreCase) || entry.Key.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        if (entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        return entry.Subtitle.Contains(text, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }
}
