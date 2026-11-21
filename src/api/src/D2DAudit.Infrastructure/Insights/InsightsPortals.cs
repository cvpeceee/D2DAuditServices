using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Insights;

public sealed class InsightsPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    InsightsEngine engine,
    IActivityWriter activity,
    TimeProvider time,
    ILogger<InsightsPortal> logger) : IInsights
{
    public async Task<Result<TrendsResponse>> TrendsAsync(int days, CancellationToken cancellationToken)
    {
        var span = Math.Clamp(days <= 0 ? 90 : days, 7, 365);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(1 - span);
        await using var db = InsightsData.Open(tenant, databases);
        var rows = new List<PostureSnapshot>();
        try
        {
            rows = await db.PostureSnapshots.AsNoTracking().Where(item => item.Day >= from).OrderBy(item => item.Day).ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Posture history is unavailable; returning the live reading only.");
        }

        if (rows.All(item => item.Day != today))
        {
            rows.Add(PostureRules.Compute(await InsightsEngine.PostureInputAsync(db, cancellationToken), today, now));
        }

        return Result<TrendsResponse>.Ok(new TrendsResponse(span, rows.Select(ToPoint).ToArray()));
    }

    public async Task<Result<TrendPoint>> RecordAsync(CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<TrendPoint>.Fail("forbidden");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var members = await InsightsEngine.MembersAsync(control, tenant.TenantId, cancellationToken);
        await engine.RefreshAsync(tenant.TenantId, db, members, cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var snapshot = await db.PostureSnapshots.AsNoTracking().FirstAsync(item => item.Day == today, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "insights.snapshot", "ok", snapshot.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
        return Result<TrendPoint>.Ok(ToPoint(snapshot));
    }

    public static TrendPoint ToPoint(PostureSnapshot row) => new(
        row.Day,
        row.ControlsPassing,
        row.ControlsTotal,
        PostureRules.Percent(row.ControlsPassing, row.ControlsTotal),
        row.FailingTests,
        row.VulnCritical,
        row.VulnHigh,
        row.VulnMedium,
        row.VulnLow,
        row.OpenTasks,
        row.OverdueItems,
        PostureRules.Frameworks(row).Select(item => new TrendFramework(item.Key, item.Name, item.Passing, item.Total, PostureRules.Percent(item.Passing, item.Total))).ToArray());
}

public sealed class NotificationCenter(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    InsightsEngine engine,
    IActivityWriter activity,
    TimeProvider time) : INotificationCenter
{
    private const int Window = 500;

    public async Task<NotificationFeed> FeedAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var (items, unread) = await VisibleAsync(db, cancellationToken);
        return new NotificationFeed(unread, items.Take(Math.Clamp(take <= 0 ? 50 : take, 1, 100)).ToArray());
    }

    public async Task<UnreadCount> UnreadAsync(CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var (_, unread) = await VisibleAsync(db, cancellationToken);
        return new UnreadCount(unread);
    }

    public async Task<Result<UnreadCount>> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var (items, _) = await VisibleAsync(db, cancellationToken);
        var item = items.FirstOrDefault(entry => entry.Id == id);
        if (item is null)
        {
            return Result<UnreadCount>.Fail("not_found");
        }

        if (!item.Read)
        {
            db.NotificationReceipts.Add(new NotificationReceipt { Id = Guid.NewGuid(), NotificationId = id, Email = Me, ReadAt = time.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<UnreadCount>.Ok(new UnreadCount(items.Count(entry => !entry.Read && entry.Id != id)));
    }

    public async Task<UnreadCount> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var (items, _) = await VisibleAsync(db, cancellationToken);
        var now = time.GetUtcNow();
        db.NotificationReceipts.AddRange(items.Where(item => !item.Read).Select(item => new NotificationReceipt { Id = Guid.NewGuid(), NotificationId = item.Id, Email = Me, ReadAt = now }));
        await db.SaveChangesAsync(cancellationToken);
        return new UnreadCount(0);
    }

    public async Task<IReadOnlyList<NotificationPreferenceItem>> PreferencesAsync(CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var rows = await db.NotificationPreferences.AsNoTracking().Where(item => item.Email == Me).ToListAsync(cancellationToken);
        return NotificationKinds.All
            .Select(kind => InsightsEngine.Preference(rows, Me, kind))
            .Select(item => new NotificationPreferenceItem(item.Kind, item.InApp, item.EmailDigest, item.Slack))
            .ToArray();
    }

    public async Task<Result<IReadOnlyList<NotificationPreferenceItem>>> SavePreferencesAsync(SavePreferencesRequest request, CancellationToken cancellationToken)
    {
        var items = request.Preferences ?? [];
        if (items.Count is 0 or > 20 || items.Any(item => !NotificationKinds.All.Contains(item.Kind)))
        {
            return Result<IReadOnlyList<NotificationPreferenceItem>>.Fail("invalid_preferences");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var rows = await db.NotificationPreferences.Where(item => item.Email == Me).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            var row = rows.FirstOrDefault(entry => entry.Kind == item.Kind);
            if (row is null)
            {
                row = new NotificationPreference { Id = Guid.NewGuid(), Email = Me, Kind = item.Kind };
                db.NotificationPreferences.Add(row);
                rows.Add(row);
            }

            row.InApp = item.InApp;
            row.EmailDigest = item.Email;
            row.Slack = item.Slack;
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "notifications.preferences", "ok", null, null, cancellationToken);
        return Result<IReadOnlyList<NotificationPreferenceItem>>.Ok(NotificationKinds.All
            .Select(kind => InsightsEngine.Preference(rows, Me, kind))
            .Select(item => new NotificationPreferenceItem(item.Kind, item.InApp, item.EmailDigest, item.Slack))
            .ToArray());
    }

    public async Task<Result<DigestResult>> SendDigestAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<DigestResult>.Fail("forbidden");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var members = await InsightsEngine.MembersAsync(control, tenant.TenantId, cancellationToken);
        var result = await engine.DigestAsync(db, tenant.TenantName, members, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "notifications.digest", "ok", result.Recipients + " recipients", null, cancellationToken);
        return Result<DigestResult>.Ok(result);
    }

    private string Me => NotificationRules.Normalize(tenant.Email);

    private async Task<(IReadOnlyList<NotificationItem> Items, int Unread)> VisibleAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var email = Me;
        var auditor = tenant.IsAuditor;
        var rows = await db.UserNotifications.AsNoTracking()
            .Where(item => item.RecipientEmail == email || (item.RecipientEmail == null && !auditor))
            .OrderByDescending(item => item.At)
            .Take(Window)
            .ToListAsync(cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(item => item.Email == email).ToListAsync(cancellationToken);
        var ids = rows.Select(item => item.Id).ToArray();
        var read = (await db.NotificationReceipts.AsNoTracking()
                .Where(item => item.Email == email && ids.Contains(item.NotificationId))
                .Select(item => item.NotificationId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var items = rows
            .Where(item => InsightsEngine.Preference(preferences, email, item.Kind).InApp)
            .Select(item => new NotificationItem(item.Id, item.Kind, item.Title, item.Body, item.Href, item.At, read.Contains(item.Id)))
            .ToArray();
        return (items, items.Count(item => !item.Read));
    }
}

public sealed class GlobalSearch(TenantContext tenant, TenantDatabases databases) : IGlobalSearch
{
    public async Task<SearchResponse> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var text = (query ?? "").Trim();
        if (text.Length is 0 or > 100)
        {
            return new SearchResponse(text, []);
        }

        await using var db = InsightsData.Open(tenant, databases);
        var entries = await EntriesAsync(db, tenant.Role, cancellationToken);
        var results = SearchRules.Filter(entries, text, tenant.Role, tenant.AuditorFrameworkKeys)
            .Select(item => new SearchResult(item.Kind, item.Key, item.Title, item.Subtitle, item.Href))
            .ToArray();
        return new SearchResponse(text, results);
    }

    public static async Task<IReadOnlyList<SearchEntry>> EntriesAsync(TenantDbContext db, TenantRole role, CancellationToken cancellationToken)
    {
        bool Wants(string kind) => role switch
        {
            TenantRole.Auditor => SearchRules.AuditorKinds.Contains(kind),
            TenantRole.Employee => SearchRules.EmployeeKinds.Contains(kind),
            _ => true,
        };

        var entries = new List<SearchEntry>();
        var frameworks = await db.Frameworks.AsNoTracking().Select(item => new { item.Key, item.Name, item.Version }).ToListAsync(cancellationToken);
        entries.AddRange(frameworks.Select(item => new SearchEntry("framework", item.Key, item.Name, item.Version, "/frameworks/" + item.Key, [item.Key])));
        var controlRows = await db.Controls.AsNoTracking()
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.Title,
                item.Status,
                item.OwnerName,
                Keys = item.Mappings.Select(mapping => mapping.Requirement!.Framework!.Key).Distinct().ToList(),
            })
            .ToListAsync(cancellationToken);
        var keysByCode = controlRows.ToDictionary(item => item.Code, item => (IReadOnlyList<string>)item.Keys, StringComparer.Ordinal);
        var controlsById = controlRows.ToDictionary(item => item.Id);
        if (Wants("control"))
        {
            entries.AddRange(controlRows.Select(item => new SearchEntry("control", item.Code, item.Title, item.Status + (string.IsNullOrWhiteSpace(item.OwnerName) ? "" : " Â· " + item.OwnerName), "/controls/" + item.Code, item.Keys)));
        }

        if (Wants("test"))
        {
            var tests = await db.ControlTests.AsNoTracking().Select(item => new { item.ControlId, item.Name, item.Outcome }).ToListAsync(cancellationToken);
            entries.AddRange(tests
                .Where(item => controlsById.ContainsKey(item.ControlId))
                .Select(item =>
                {
                    var owner = controlsById[item.ControlId];
                    return new SearchEntry("test", owner.Code, item.Name, owner.Code + " Â· " + item.Outcome, "/controls/" + owner.Code, owner.Keys);
                }));
        }

        if (Wants("evidence"))
        {
            var evidence = await db.Evidence.AsNoTracking()
                .OrderByDescending(item => item.CollectedAt)
                .Take(2000)
                .Select(item => new { item.ControlId, item.Title, item.Source })
                .ToListAsync(cancellationToken);
            entries.AddRange(evidence
                .Where(item => controlsById.ContainsKey(item.ControlId))
                .Select(item =>
                {
                    var owner = controlsById[item.ControlId];
                    return new SearchEntry("evidence", owner.Code, item.Title, item.Source + " Â· " + owner.Code, "/evidence", owner.Keys);
                }));
        }

        if (Wants("policy"))
        {
            var policies = await db.Policies.AsNoTracking()
                .Select(item => new { item.Code, item.Title, item.OwnerName, Controls = item.Controls.Select(link => link.ControlCode).ToList() })
                .ToListAsync(cancellationToken);
            entries.AddRange(policies.Select(item => new SearchEntry(
                "policy",
                item.Code,
                item.Title,
                item.OwnerName,
                "/policies",
                item.Controls.SelectMany(code => keysByCode.TryGetValue(code, out var keys) ? keys : []).Distinct().ToArray())));
        }

        if (Wants("risk"))
        {
            var risks = await db.Risks.AsNoTracking().Select(item => new { item.Code, item.Title, item.OwnerName }).ToListAsync(cancellationToken);
            entries.AddRange(risks.Select(item => new SearchEntry("risk", item.Code, item.Title, item.OwnerName, "/risks", [])));
        }

        if (Wants("vendor"))
        {
            var vendors = await db.Vendors.AsNoTracking().Select(item => new { item.Name, item.Service }).ToListAsync(cancellationToken);
            entries.AddRange(vendors.Select(item => new SearchEntry("vendor", item.Name, item.Name, item.Service, "/vendors", [])));
        }

        if (Wants("person"))
        {
            var people = await db.People.AsNoTracking().Select(item => new { item.Name, item.Email, item.Department }).ToListAsync(cancellationToken);
            entries.AddRange(people.Select(item => new SearchEntry("person", item.Email, item.Name, item.Department + " Â· " + item.Email, "/people", [])));
        }

        return entries;
    }
}

public sealed class ReportsPortal(
    TenantContext tenant,
    TenantDatabases databases,
    INotifier notifier,
    IActivityWriter activity,
    TimeProvider time) : IReports
{
    private static readonly string[] Frequencies = ["Daily", "Weekly", "Monthly"];

    public async Task<Result<FileExport>> ExportAsync(string key, string? format, string? framework, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor || tenant.Role == TenantRole.Employee)
        {
            return Result<FileExport>.Fail("forbidden");
        }

        var kind = (format ?? "pdf").Trim().ToLowerInvariant();
        if (!InsightsEngine.ReportKeys.Contains(key) || kind is not ("csv" or "pdf") || (framework?.Length ?? 0) > 64)
        {
            return Result<FileExport>.Fail("not_found");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var file = await InsightsEngine.BuildReportAsync(db, tenant.TenantName, key, kind, framework, Today, cancellationToken);
        if (file is null)
        {
            return Result<FileExport>.Fail("not_found");
        }

        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "reports.export", "ok", key + "." + kind, null, cancellationToken);
        return Result<FileExport>.Ok(file);
    }

    public async Task<IReadOnlyList<ReportScheduleItem>> SchedulesAsync(CancellationToken cancellationToken)
    {
        await using var db = InsightsData.Open(tenant, databases);
        var rows = await db.ReportSchedules.AsNoTracking().ToListAsync(cancellationToken);
        return InsightsEngine.ReportKeys.Where(key => key != "framework").Select(key =>
        {
            var row = rows.FirstOrDefault(item => item.ReportKey == key);
            return row is null
                ? new ReportScheduleItem(key, "Weekly", "pdf", "", false, null, null, null)
                : ToItem(row);
        }).ToArray();
    }

    public async Task<Result<ReportScheduleItem>> SaveScheduleAsync(SaveReportScheduleRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<ReportScheduleItem>.Fail("forbidden");
        }

        var key = (request.ReportKey ?? "").Trim();
        var frequency = (request.Frequency ?? "").Trim();
        var format = (request.Format ?? "").Trim().ToLowerInvariant();
        var recipients = Recipients(request.Recipients);
        if (!InsightsEngine.ReportKeys.Contains(key) || key == "framework" || !Frequencies.Contains(frequency) || format is not ("csv" or "pdf")
            || recipients is null || (request.Enabled && recipients.Count == 0))
        {
            return Result<ReportScheduleItem>.Fail("invalid_schedule");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var row = await db.ReportSchedules.FirstOrDefaultAsync(item => item.ReportKey == key, cancellationToken);
        if (row is null)
        {
            row = new ReportSchedule { Id = Guid.NewGuid(), ReportKey = key };
            db.ReportSchedules.Add(row);
        }

        row.Frequency = frequency;
        row.Format = format;
        row.Recipients = string.Join(", ", recipients);
        row.Enabled = request.Enabled;
        row.NextRunOn = InsightsEngine.NextRun(frequency, Today);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "reports.schedule", "ok", key + " " + frequency + (request.Enabled ? "" : " (off)"), null, cancellationToken);
        return Result<ReportScheduleItem>.Ok(ToItem(row));
    }

    public async Task<Result<ReportDelivery>> SendNowAsync(string key, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<ReportDelivery>.Fail("forbidden");
        }

        await using var db = InsightsData.Open(tenant, databases);
        var row = await db.ReportSchedules.FirstOrDefaultAsync(item => item.ReportKey == key, cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.Recipients))
        {
            return Result<ReportDelivery>.Fail("not_found");
        }

        var delivery = await ReportDeliveries.DeliverAsync(db, notifier, tenant.TenantName, row, Today, time.GetUtcNow(), cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "reports.send", "ok", key + " " + delivery, null, cancellationToken);
        return Result<ReportDelivery>.Ok(new ReportDelivery(key, delivery, row.LastSentAt ?? time.GetUtcNow()));
    }

    public static IReadOnlyList<string>? Recipients(string? value)
    {
        var list = (value ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NotificationRules.Normalize)
            .Distinct()
            .ToArray();
        return list.Length > 20 || list.Any(item => item.Length > 254 || item.Count(character => character == '@') != 1 || item.StartsWith('@') || item.EndsWith('@'))
            ? null
            : list;
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private static ReportScheduleItem ToItem(ReportSchedule row) =>
        new(row.ReportKey, row.Frequency, row.Format, row.Recipients, row.Enabled, row.NextRunOn, row.LastSentAt, row.LastDeliveryId);
}

internal static class ReportDeliveries
{
    /// <summary>Builds the report and sends it through the notifier. Without a mail provider the notifier returns a mock delivery id.</summary>
    public static async Task<string> DeliverAsync(TenantDbContext db, INotifier notifier, string tenantName, ReportSchedule row, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var file = await InsightsEngine.BuildReportAsync(db, tenantName, row.ReportKey, row.Format, null, today, cancellationToken);
        var detail = file is null
            ? "The report could not be built."
            : $"{file.FileName} ({file.Body.Length} bytes) is ready in D2D Audit under Reports.";
        var ids = new List<string>();
        foreach (var recipient in ReportsPortal.Recipients(row.Recipients) ?? [])
        {
            ids.Add(await notifier.SendAsync("email", recipient, $"Scheduled {row.ReportKey} report for {tenantName}", detail, cancellationToken));
        }

        row.LastSentAt = now;
        row.LastDeliveryId = ids.FirstOrDefault() ?? "none";
        row.NextRunOn = InsightsEngine.NextRun(row.Frequency, today);
        await db.SaveChangesAsync(cancellationToken);
        return row.LastDeliveryId;
    }
}

internal static class InsightsData
{
    public static TenantDbContext Open(TenantContext tenant, TenantDatabases databases)
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
