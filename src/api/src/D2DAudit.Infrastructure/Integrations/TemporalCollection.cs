using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace D2DAudit.Infrastructure.Integrations;

[Workflow]
public class CollectionWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(Guid tenantId, Guid jobId) =>
        await Workflow.ExecuteActivityAsync(
            (CollectionActivities activities) => activities.RunAsync(tenantId, jobId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(10) });
}

[Workflow]
public class DailyCollectionWorkflow
{
    [WorkflowRun]
    public async Task RunAsync()
    {
        var tenants = await Workflow.ExecuteActivityAsync(
            (CollectionActivities activities) => activities.ListTenantIdsAsync(),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) });
        foreach (var tenantId in tenants)
        {
            await Workflow.ExecuteActivityAsync(
                (CollectionActivities activities) => activities.CollectTenantAsync(tenantId),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(15) });
        }
    }
}

public sealed class CollectionActivities(CollectionRunner runner, CollectionJobs jobs, ControlPlaneDbContext control)
{
    [Activity]
    public Task RunAsync(Guid tenantId, Guid jobId) =>
        runner.RunAsync(tenantId, jobId, ActivityExecutionContext.Current.CancellationToken);

    [Activity]
    public async Task CollectTenantAsync(Guid tenantId)
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        var jobId = await jobs.CreateAsync(tenantId, cancellationToken);
        await runner.RunAsync(tenantId, jobId, cancellationToken);
    }

    [Activity]
    public Task<Guid[]> ListTenantIdsAsync()
    {
        var cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        return control.Tenants.AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active && tenant.DatabaseName != null)
            .Select(tenant => tenant.Id)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed class TemporalCollectionSchedule(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<TemporalCollectionSchedule> logger) : IHostedService, ICollectionSchedule
{
    public const string TaskQueue = "d2d-collection";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            try
            {
                await EnsureScheduleAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is RpcException or TimeoutException or HttpRequestException)
            {
                last = exception;
                logger.LogWarning(exception, "Waiting for Temporal, attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        if (environment.IsProduction() || environment.IsStaging())
        {
            throw new InvalidOperationException("Temporal did not become ready.", last);
        }

        logger.LogWarning(last, "Temporal schedule was not created.");
    }

    public async Task<Result<Guid>> StartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var jobId = await scope.ServiceProvider.GetRequiredService<CollectionJobs>().CreateAsync(tenantId, cancellationToken);
        var client = scope.ServiceProvider.GetRequiredService<ITemporalClient>();
        await client.StartWorkflowAsync(
            (CollectionWorkflow workflow) => workflow.RunAsync(tenantId, jobId),
            new WorkflowOptions { Id = "collect-" + jobId.ToString("N"), TaskQueue = TaskQueue });
        return Result<Guid>.Ok(jobId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureScheduleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITemporalClient>();
        var handle = client.GetScheduleHandle("d2d-daily-collection");
        try
        {
            _ = await handle.DescribeAsync();
            return;
        }
        catch (RpcException)
        {
            // The schedule is created below.
        }

        var schedule = new Schedule(
            ScheduleActionStartWorkflow.Create(
                (DailyCollectionWorkflow workflow) => workflow.RunAsync(),
                new WorkflowOptions { Id = "d2d-daily", TaskQueue = TaskQueue }),
            new ScheduleSpec { Intervals = [new ScheduleIntervalSpec(TimeSpan.FromDays(1))] });
        try
        {
            _ = await client.CreateScheduleAsync("d2d-daily-collection", schedule);
        }
        catch (ScheduleAlreadyRunningException exception)
        {
            logger.LogInformation(exception, "Daily collection schedule already exists.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
