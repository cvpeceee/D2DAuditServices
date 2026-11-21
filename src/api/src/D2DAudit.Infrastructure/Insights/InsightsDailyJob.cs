using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Insights;

/// <summary>
/// In-process daily job: records each tenant's posture snapshot, raises notifications, emails the digest,
/// and delivers scheduled reports. It checks hourly and does the work once per UTC day.
/// </summary>
public sealed class InsightsDailyJob(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<InsightsDailyJob> logger) : BackgroundService
{
    private DateOnly _lastRun = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            if (today != _lastRun)
            {
                try
                {
                    await RunAsync(stoppingToken);
                    _lastRun = today;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "The daily insights run failed; it will retry within the hour.");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var databases = scope.ServiceProvider.GetRequiredService<TenantDatabases>();
        var engine = scope.ServiceProvider.GetRequiredService<InsightsEngine>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var tenants = await control.Tenants.AsNoTracking()
            .Where(item => item.Status == TenantStatus.Active)
            .Select(item => new { item.Id, item.Name, DatabaseName = item.DatabaseName ?? "" })
            .ToListAsync(cancellationToken);
        foreach (var tenant in tenants.Where(item => DatabaseNames.IsSafe(item.DatabaseName)))
        {
            try
            {
                await using var db = databases.Create(tenant.DatabaseName);
                var members = await InsightsEngine.MembersAsync(control, tenant.Id, cancellationToken);
                await engine.RefreshAsync(tenant.Id, db, members, cancellationToken);
                await engine.DigestAsync(db, tenant.Name, members, cancellationToken);
                var now = time.GetUtcNow();
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                var due = await db.ReportSchedules.Where(item => item.Enabled && item.NextRunOn <= today).ToListAsync(cancellationToken);
                foreach (var schedule in due)
                {
                    await ReportDeliveries.DeliverAsync(db, notifier, tenant.Name, schedule, today, now, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Daily insights failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }
}
