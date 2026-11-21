using System.Globalization;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Insights;

public sealed record InsightMember(string Email, string Name, TenantRole Role);

/// <summary>
/// Posture snapshots, notification generation, the daily digest, and report bodies.
/// Works on an open tenant database so the API, the collection run, and the daily job share one code path.
/// </summary>
public sealed class InsightsEngine(
    INotifier notifier,
    INotificationPush push,
    TimeProvider time,
    ILogger<InsightsEngine> logger)
{
    public static readonly IReadOnlyList<string> ReportKeys = ["board", "framework", "risk", "vendor", "personnel"];

    public async Task<(PostureSnapshot Snapshot, IReadOnlyList<Guid> NewlyFailing)> SnapshotAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var input = await PostureInputAsync(db, cancellationToken);
        var computed = PostureRules.Compute(input, today, now);
        var baseline = await db.PostureSnapshots.AsNoTracking()
            .Where(item => item.Day <= today)
            .OrderByDescending(item => item.Day)
            .FirstOrDefaultAsync(cancellationToken);
        var before = PostureRules.FailingTests(baseline).ToHashSet();
        var newlyFailing = input.FailingTestIds.Where(id => !before.Contains(id)).ToArray();
        var existing = await db.PostureSnapshots.FirstOrDefaultAsync(item => item.Day == today, cancellationToken);
        if (existing is null)
        {
            db.PostureSnapshots.Add(computed);
            existing = computed;
        }
        else
        {
            existing.ControlsPassing = computed.ControlsPassing;
            existing.ControlsTotal = computed.ControlsTotal;
            existing.FailingTests = computed.FailingTests;
            existing.VulnCritical = computed.VulnCritical;
            existing.VulnHigh = computed.VulnHigh;
            existing.VulnMedium = computed.VulnMedium;
            existing.VulnLow = computed.VulnLow;
            existing.OpenTasks = computed.OpenTasks;
            existing.OverdueItems = computed.OverdueItems;
            existing.FrameworksJson = computed.FrameworksJson;
            existing.FailingTestIdsJson = computed.FailingTestIdsJson;
            existing.RecordedAt = computed.RecordedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (existing, newlyFailing);
    }

    /// <summary>Records today's snapshot, raises new notifications, and pushes them live.</summary>
    public async Task<int> RefreshAsync(Guid tenantId, TenantDbContext db, IReadOnlyList<InsightMember> members, CancellationToken cancellationToken)
    {
        var (_, newlyFailing) = await SnapshotAsync(db, cancellationToken);
        var facts = await FactsAsync(db, newlyFailing, cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var drafts = NotificationRules.Generate(facts, today);
        var created = await StoreAsync(db, drafts, cancellationToken);
        if (created.Count == 0)
        {
            return 0;
        }

        var preferences = await db.NotificationPreferences.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var note in created)
        {
            var targets = Targets(note, members);
            var slack = targets.Any(member => Preference(preferences, member.Email, note.Kind).Slack);
            if (slack)
            {
                await notifier.SendAsync("slack", note.Kind, note.Title, note.Body + "\n" + note.Href, cancellationToken);
            }
        }

        await push.NotifyAsync(tenantId, created.Count, cancellationToken);
        return created.Count;
    }

    /// <summary>Emails each member a digest of their unread notifications from the last day, honouring preferences.</summary>
    public async Task<DigestResult> DigestAsync(TenantDbContext db, string tenantName, IReadOnlyList<InsightMember> members, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().AddDays(-1);
        var recent = await db.UserNotifications.AsNoTracking().Where(item => item.At >= since).OrderByDescending(item => item.At).ToListAsync(cancellationToken);
        var ids = recent.Select(item => item.Id).ToArray();
        var receipts = await db.NotificationReceipts.AsNoTracking().Where(item => ids.Contains(item.NotificationId)).ToListAsync(cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().ToListAsync(cancellationToken);
        var deliveries = new List<string>();
        var total = 0;
        foreach (var member in members)
        {
            var email = NotificationRules.Normalize(member.Email);
            var items = recent
                .Where(item => NotificationRules.Visible(item.RecipientEmail, email, member.Role == TenantRole.Auditor))
                .Where(item => Preference(preferences, email, item.Kind).EmailDigest)
                .Where(item => !receipts.Any(receipt => receipt.NotificationId == item.Id && receipt.Email == email))
                .ToArray();
            if (items.Length == 0)
            {
                continue;
            }

            var body = new StringBuilder();
            body.AppendLine(CultureInfo.InvariantCulture, $"Hello {member.Name},");
            body.AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture, $"You have {items.Length} unread notifications in {tenantName}:");
            foreach (var item in items)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"- {item.Title} ({item.Href})");
            }

            deliveries.Add(await notifier.SendAsync("email", email, "Your daily D2D Audit digest", body.ToString(), cancellationToken));
            total += items.Length;
        }

        return new DigestResult(deliveries.Count, total, deliveries);
    }

    public static async Task<IReadOnlyList<InsightMember>> MembersAsync(ControlPlaneDbContext control, Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await control.Memberships.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Active)
            .Select(item => new { item.User!.Email, item.User.DisplayName, item.Role })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new InsightMember(row.Email, row.DisplayName, row.Role)).ToArray();
    }

    public static async Task<PostureInput> PostureInputAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .Include(item => item.Tests)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var posture = controls.Select(item => new PostureControl(
            item.Status,
            item.Mappings
                .Where(mapping => mapping.Requirement?.Framework is not null)
                .Select(mapping => (mapping.Requirement!.Framework!.Key, mapping.Requirement.Framework.Name))
                .Distinct()
                .ToArray())).ToArray();
        var failing = controls.SelectMany(item => item.Tests).Where(test => test.Outcome == TestOutcome.Fail).Select(test => test.Id).ToArray();
        var vulns = await db.Vulnerabilities.AsNoTracking().Where(item => !item.Ignored).Select(item => item.Severity).ToListAsync(cancellationToken);
        var tasks = await db.WorkTasks.AsNoTracking().Where(item => item.Status == "Open").Select(item => item.DueOn).ToListAsync(cancellationToken);
        var other = new List<DateOnly>();
        other.AddRange(await db.Vendors.AsNoTracking().Select(item => item.ReviewDueOn).ToListAsync(cancellationToken));
        other.AddRange(await db.Risks.AsNoTracking().Select(item => item.ReviewOn).ToListAsync(cancellationToken));
        other.AddRange(await db.Acknowledgements.AsNoTracking().Where(item => item.AcknowledgedAt == null).Select(item => item.DueOn).ToListAsync(cancellationToken));
        other.AddRange((await db.People.AsNoTracking()
                .Where(item => item.State == PersonState.Active && item.Training != TrainingState.Complete && item.TrainingDueOn != null)
                .Select(item => item.TrainingDueOn)
                .ToListAsync(cancellationToken))
            .Select(item => item!.Value));
        return new PostureInput(posture, failing, vulns, tasks, other);
    }

    public static async Task<NotificationFacts> FactsAsync(TenantDbContext db, IReadOnlyCollection<Guid> newlyFailing, CancellationToken cancellationToken)
    {
        var ids = newlyFailing.ToArray();
        var failingTests = ids.Length == 0
            ? []
            : await db.ControlTests.AsNoTracking()
                .Where(test => ids.Contains(test.Id))
                .Join(db.Controls.AsNoTracking(), test => test.ControlId, control => control.Id, (test, control) => new FailingTestFact(test.Id, test.Name, control.Code, control.OwnerEmail ?? "", test.Detail ?? ""))
                .ToListAsync(cancellationToken);
        var tasks = await db.WorkTasks.AsNoTracking()
            .Where(item => item.Status == "Open")
            .Select(item => new TaskFact(item.Id, item.Title, item.OwnerEmail, item.DueOn, item.Status))
            .ToListAsync(cancellationToken);
        var acknowledgements = await db.Acknowledgements.AsNoTracking()
            .Where(item => item.AcknowledgedAt == null && item.Version!.State == PolicyState.Approved)
            .Select(item => new AckFact(item.PolicyVersionId, item.Version!.Policy!.Code, item.Version.Policy.Title, item.PersonEmail, item.DueOn, false))
            .ToListAsync(cancellationToken);
        var reviews = new List<ReviewFact>();
        reviews.AddRange(await db.Risks.AsNoTracking()
            .Select(item => new ReviewFact("Risk", item.Code, item.Code + " " + item.Title, item.OwnerEmail, item.ReviewOn, "/risks"))
            .ToListAsync(cancellationToken));
        reviews.AddRange((await db.People.AsNoTracking()
                .Where(item => item.State == PersonState.Active && item.Training != TrainingState.Complete && item.TrainingDueOn != null)
                .Select(item => new { item.Id, item.Email, item.TrainingDueOn })
                .ToListAsync(cancellationToken))
            .Select(item => new ReviewFact("Training", item.Id.ToString("N"), "Security awareness training", item.Email, item.TrainingDueOn!.Value, "/me")));
        var requests = await db.SampleRequests.AsNoTracking()
            .Select(item => new RequestFact(item.Id, item.Code, item.Title, item.DueOn, item.Status == SampleStatus.Open))
            .ToListAsync(cancellationToken);
        var vendors = await db.Vendors.AsNoTracking()
            .Select(item => new VendorFact(item.Id, item.Name, item.ReviewDueOn))
            .ToListAsync(cancellationToken);
        return new NotificationFacts(failingTests, tasks, acknowledgements, reviews, requests, vendors);
    }

    public static NotificationPreference Preference(IEnumerable<NotificationPreference> preferences, string email, string kind)
    {
        var normalized = NotificationRules.Normalize(email);
        return preferences.FirstOrDefault(item => item.Email == normalized && item.Kind == kind)
            ?? new NotificationPreference { Email = normalized, Kind = kind, InApp = true, EmailDigest = true, Slack = false };
    }

    public static async Task<FileExport?> BuildReportAsync(
        TenantDbContext db,
        string tenantName,
        string key,
        string format,
        string? frameworkKey,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!ReportKeys.Contains(key) || format is not ("csv" or "pdf"))
        {
            return null;
        }

        string title;
        string[] header;
        var rows = new List<string[]>();
        var summary = new List<string>();
        switch (key)
        {
            case "board":
            case "framework":
            {
                var input = await PostureInputAsync(db, cancellationToken);
                var snapshot = PostureRules.Compute(input, today, DateTimeOffset.UtcNow);
                var frameworks = PostureRules.Frameworks(snapshot)
                    .Where(item => key == "board" || string.IsNullOrWhiteSpace(frameworkKey) || item.Key == frameworkKey)
                    .ToArray();
                if (key == "framework" && frameworks.Length == 0)
                {
                    return null;
                }

                title = key == "board" ? "Board and executive report" : "Framework readiness report";
                summary.Add("Overall readiness " + Num(PostureRules.Percent(snapshot.ControlsPassing, snapshot.ControlsTotal)) + "%  (" + Num(snapshot.ControlsPassing) + "/" + Num(snapshot.ControlsTotal) + " controls passing)");
                summary.Add("Failing tests " + Num(snapshot.FailingTests) + "  Open tasks " + Num(snapshot.OpenTasks) + "  Overdue items " + Num(snapshot.OverdueItems));
                summary.Add("Open vulnerabilities critical " + Num(snapshot.VulnCritical) + "  high " + Num(snapshot.VulnHigh) + "  medium " + Num(snapshot.VulnMedium) + "  low " + Num(snapshot.VulnLow));
                header = ["framework", "name", "passing", "total", "percent"];
                rows.AddRange(frameworks.Select(item => new[] { item.Key, item.Name, Num(item.Passing), Num(item.Total), Num(PostureRules.Percent(item.Passing, item.Total)) }));
                if (key == "framework")
                {
                    var selected = frameworks[0];
                    var controls = await db.Controls.AsNoTracking()
                        .Where(item => item.Mappings.Any(mapping => mapping.Requirement!.Framework!.Key == selected.Key))
                        .OrderBy(item => item.Code)
                        .Select(item => new { item.Code, item.Title, item.Status, item.OwnerName })
                        .ToListAsync(cancellationToken);
                    header = ["control", "title", "status", "owner"];
                    rows = controls.Select(item => new[] { item.Code, item.Title, item.Status.ToString(), item.OwnerName ?? "" }).ToList();
                    title += " - " + selected.Name;
                    summary.Add(selected.Name + " " + Num(PostureRules.Percent(selected.Passing, selected.Total)) + "%  (" + Num(selected.Passing) + "/" + Num(selected.Total) + ")");
                }

                break;
            }

            case "risk":
            {
                title = "Risk report";
                var risks = await db.Risks.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
                header = ["code", "title", "likelihood", "impact", "score", "treatment", "owner", "reviewOn"];
                rows.AddRange(risks.Select(item => new[] { item.Code, item.Title, Num(item.Likelihood), Num(item.Impact), Num(item.Likelihood * item.Impact), item.Treatment.ToString(), item.OwnerName, Day(item.ReviewOn) }));
                summary.Add(Num(risks.Count) + " risks, " + Num(risks.Count(item => item.Likelihood * item.Impact >= 15)) + " high, " + Num(risks.Count(item => item.ReviewOn < today)) + " reviews overdue");
                break;
            }

            case "vendor":
            {
                title = "Vendor report";
                var vendors = await db.Vendors.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
                header = ["vendor", "service", "criticality", "reviewState", "reviewDueOn", "riskRating"];
                rows.AddRange(vendors.Select(item => new[] { item.Name, item.Service, item.Criticality.ToString(), item.ReviewState.ToString(), Day(item.ReviewDueOn), Num(item.RiskRating) }));
                summary.Add(Num(vendors.Count) + " vendors, " + Num(vendors.Count(item => item.ReviewDueOn < today)) + " reviews overdue");
                break;
            }

            default:
            {
                title = "Personnel compliance report";
                var people = await db.People.AsNoTracking().Where(item => item.State != PersonState.Offboarded).OrderBy(item => item.Name).ToListAsync(cancellationToken);
                var pending = (await db.Acknowledgements.AsNoTracking().Where(item => item.AcknowledgedAt == null).Select(item => item.PersonEmail).ToListAsync(cancellationToken))
                    .Select(NotificationRules.Normalize)
                    .ToArray();
                header = ["name", "email", "department", "state", "training", "trainingDueOn", "pendingPolicies"];
                rows.AddRange(people.Select(item => new[]
                {
                    item.Name,
                    item.Email,
                    item.Department,
                    item.State.ToString(),
                    item.Training.ToString(),
                    item.TrainingDueOn is DateOnly due ? Day(due) : "",
                    Num(pending.Count(email => email == NotificationRules.Normalize(item.Email))),
                }));
                var compliant = people.Count(item => item.Training == TrainingState.Complete && !pending.Contains(NotificationRules.Normalize(item.Email)));
                summary.Add(Num(compliant) + " of " + Num(people.Count) + " people fully compliant");
                break;
            }
        }

        var slug = key == "framework" && !string.IsNullOrWhiteSpace(frameworkKey) ? key + "-" + Safe(frameworkKey) : key;
        if (format == "csv")
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', header));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(',', row.Select(Cell)));
            }

            return new FileExport(slug + "-report.csv", "text/csv", Encoding.UTF8.GetBytes(csv.ToString()));
        }

        var lines = new List<string> { "D2D Audit Services  " + title, tenantName + "  " + Day(today), "" };
        lines.AddRange(summary);
        lines.Add("");
        lines.Add(string.Join("  |  ", header));
        lines.AddRange(rows.Select(row => string.Join("  |  ", row)));
        return new FileExport(slug + "-report.pdf", "application/pdf", PlainPdf.Document(lines));
    }

    public static DateOnly NextRun(string frequency, DateOnly from) => frequency switch
    {
        "Daily" => from.AddDays(1),
        "Monthly" => from.AddMonths(1),
        _ => from.AddDays(7),
    };

    public static string Cell(string value)
    {
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private async Task<IReadOnlyList<UserNotification>> StoreAsync(TenantDbContext db, IReadOnlyList<NotificationDraft> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        var keys = drafts.Select(item => item.DedupeKey).ToArray();
        var known = (await db.UserNotifications.AsNoTracking().Where(item => keys.Contains(item.DedupeKey)).Select(item => item.DedupeKey).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var now = time.GetUtcNow();
        var created = drafts
            .Where(item => !known.Contains(item.DedupeKey))
            .Select(item => new UserNotification
            {
                Id = Guid.NewGuid(),
                RecipientEmail = item.RecipientEmail,
                Kind = item.Kind,
                Title = item.Title,
                Body = item.Body,
                Href = item.Href,
                DedupeKey = item.DedupeKey,
                At = now,
            })
            .ToArray();
        db.UserNotifications.AddRange(created);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Raised {Count} notifications.", created.Length);
        return created;
    }

    private static IEnumerable<InsightMember> Targets(UserNotification note, IReadOnlyList<InsightMember> members) =>
        members.Where(member => NotificationRules.Visible(note.RecipientEmail, member.Email, member.Role == TenantRole.Auditor));

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Day(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Safe(string value) => new(value.Where(char.IsLetterOrDigit).Take(32).ToArray());
}
