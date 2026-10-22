using System.Threading.Channels;
using D2DAudit.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class LocalCollectionSchedule(
    IServiceScopeFactory scopes,
    ILogger<LocalCollectionSchedule> logger) : BackgroundService, ICollectionSchedule
{
    private readonly Channel<CollectionRequest> _channel = Channel.CreateUnbounded<CollectionRequest>();

    public async Task<Result<Guid>> StartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<CollectionJobs>();
        var jobId = await jobs.CreateAsync(tenantId, cancellationToken);
        await _channel.Writer.WriteAsync(new CollectionRequest(tenantId, jobId), cancellationToken);
        return Result<Guid>.Ok(jobId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<CollectionRunner>();
                await runner.RunAsync(request.TenantId, request.JobId, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Queued collection failed for tenant {TenantId}.", request.TenantId);
            }
        }
    }

    private readonly record struct CollectionRequest(Guid TenantId, Guid JobId);
}
