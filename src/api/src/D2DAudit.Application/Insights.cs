namespace D2DAudit.Application;

public sealed record TrendFramework(string Key, string Name, int Passing, int Total, int Percent);

public sealed record TrendPoint(
    DateOnly Day,
    int ControlsPassing,
    int ControlsTotal,
    int Percent,
    int FailingTests,
    int VulnCritical,
    int VulnHigh,
    int VulnMedium,
    int VulnLow,
    int OpenTasks,
    int OverdueItems,
    IReadOnlyList<TrendFramework> Frameworks);

public sealed record TrendsResponse(int Days, IReadOnlyList<TrendPoint> Points);

public interface IInsights
{
    Task<Result<TrendsResponse>> TrendsAsync(int days, CancellationToken cancellationToken);

    Task<Result<TrendPoint>> RecordAsync(CancellationToken cancellationToken);
}

public sealed record NotificationItem(Guid Id, string Kind, string Title, string Body, string Href, DateTimeOffset At, bool Read);

public sealed record NotificationFeed(int Unread, IReadOnlyList<NotificationItem> Items);

public sealed record UnreadCount(int Unread);

public sealed record NotificationPreferenceItem(string Kind, bool InApp, bool Email, bool Slack);

public sealed record SavePreferencesRequest(IReadOnlyList<NotificationPreferenceItem>? Preferences);

public sealed record DigestResult(int Recipients, int Notifications, IReadOnlyList<string> DeliveryIds);

public interface INotificationCenter
{
    Task<NotificationFeed> FeedAsync(int take, CancellationToken cancellationToken);

    Task<UnreadCount> UnreadAsync(CancellationToken cancellationToken);

    Task<Result<UnreadCount>> MarkReadAsync(Guid id, CancellationToken cancellationToken);

    Task<UnreadCount> MarkAllReadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationPreferenceItem>> PreferencesAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NotificationPreferenceItem>>> SavePreferencesAsync(SavePreferencesRequest request, CancellationToken cancellationToken);

    Task<Result<DigestResult>> SendDigestAsync(CancellationToken cancellationToken);
}

/// <summary>Live push of a new notification to the browsers of one tenant.</summary>
public interface INotificationPush
{
    Task NotifyAsync(Guid tenantId, int created, CancellationToken cancellationToken);
}

public sealed class NullNotificationPush : INotificationPush
{
    public Task NotifyAsync(Guid tenantId, int created, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record SearchResult(string Kind, string Key, string Title, string Subtitle, string Href);

public sealed record SearchResponse(string Query, IReadOnlyList<SearchResult> Results);

public interface IGlobalSearch
{
    Task<SearchResponse> SearchAsync(string? query, CancellationToken cancellationToken);
}

public sealed record ReportScheduleItem(
    string ReportKey,
    string Frequency,
    string Format,
    string Recipients,
    bool Enabled,
    DateOnly? NextRunOn,
    DateTimeOffset? LastSentAt,
    string? LastDeliveryId);

public sealed record SaveReportScheduleRequest(string? ReportKey, string? Frequency, string? Format, string? Recipients, bool Enabled);

public sealed record ReportDelivery(string ReportKey, string DeliveryId, DateTimeOffset SentAt);

public interface IReports
{
    Task<Result<FileExport>> ExportAsync(string key, string? format, string? framework, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportScheduleItem>> SchedulesAsync(CancellationToken cancellationToken);

    Task<Result<ReportScheduleItem>> SaveScheduleAsync(SaveReportScheduleRequest request, CancellationToken cancellationToken);

    Task<Result<ReportDelivery>> SendNowAsync(string key, CancellationToken cancellationToken);
}

public sealed record ResetPasswordBody(string? Token, string? Password);
