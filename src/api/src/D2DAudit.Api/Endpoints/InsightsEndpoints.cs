using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class InsightsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/insights/trends", Trends).RequireAuthorization("WorkspaceMember").Produces<TrendsResponse>();
        group.MapPost("/insights/snapshot", Record).RequireAuthorization("TenantWrite").Produces<TrendPoint>();

        group.MapGet("/notifications/feed", (int? take, INotificationCenter center, CancellationToken cancellationToken) => center.FeedAsync(take ?? 50, cancellationToken)).Produces<NotificationFeed>();
        group.MapGet("/notifications/unread-count", (INotificationCenter center, CancellationToken cancellationToken) => center.UnreadAsync(cancellationToken)).Produces<UnreadCount>();
        group.MapPost("/notifications/{id:guid}/read", MarkRead).Produces<UnreadCount>();
        group.MapPost("/notifications/read-all", (INotificationCenter center, CancellationToken cancellationToken) => center.MarkAllReadAsync(cancellationToken)).Produces<UnreadCount>();
        group.MapGet("/notifications/preferences", (INotificationCenter center, CancellationToken cancellationToken) => center.PreferencesAsync(cancellationToken)).Produces<IReadOnlyList<NotificationPreferenceItem>>();
        group.MapPut("/notifications/preferences", SavePreferences).Produces<IReadOnlyList<NotificationPreferenceItem>>();
        group.MapPost("/notifications/digest", Digest).RequireAuthorization("TenantAdmin").Produces<DigestResult>();

        group.MapGet("/search", (string? q, IGlobalSearch search, CancellationToken cancellationToken) => search.SearchAsync(q, cancellationToken)).Produces<SearchResponse>();

        group.MapGet("/reports/schedules", (IReports reports, CancellationToken cancellationToken) => reports.SchedulesAsync(cancellationToken)).RequireAuthorization("TenantAdmin").Produces<IReadOnlyList<ReportScheduleItem>>();
        group.MapPut("/reports/schedules", SaveSchedule).RequireAuthorization("TenantAdmin").Produces<ReportScheduleItem>();
        group.MapPost("/reports/schedules/{key}/send", SendNow).RequireAuthorization("TenantAdmin").Produces<ReportDelivery>();
        group.MapGet("/reports/{key}", Export).RequireAuthorization("WorkspaceMember");
        return app;
    }

    private static async Task<IResult> Trends(int? days, IInsights insights, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await insights.TrendsAsync(days ?? 90, cancellationToken));

    private static async Task<IResult> Record(IInsights insights, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await insights.RecordAsync(cancellationToken));

    private static async Task<IResult> MarkRead(Guid id, INotificationCenter center, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await center.MarkReadAsync(id, cancellationToken));

    private static async Task<IResult> SavePreferences(SavePreferencesRequest body, INotificationCenter center, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await center.SavePreferencesAsync(body, cancellationToken));

    private static async Task<IResult> Digest(INotificationCenter center, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await center.SendDigestAsync(cancellationToken));

    private static async Task<IResult> SaveSchedule(SaveReportScheduleRequest body, IReports reports, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await reports.SaveScheduleAsync(body, cancellationToken));

    private static async Task<IResult> SendNow(string key, IReports reports, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await reports.SendNowAsync(key, cancellationToken));

    private static async Task<IResult> Export(string key, string? format, string? framework, IReports reports, CancellationToken cancellationToken)
    {
        var result = await reports.ExportAsync(key, format, framework, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }
}
