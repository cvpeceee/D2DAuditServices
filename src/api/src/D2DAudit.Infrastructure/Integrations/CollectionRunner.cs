using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class CollectionJobs(ControlPlaneDbContext control, TimeProvider time, IJobNotifier notifier)
{
    public Task<Guid> CreateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        CreateAsync(tenantId, "collection", "Queued", cancellationToken);

    public async Task<Guid> CreateAsync(Guid tenantId, string kind, string queuedStep, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var job = new JobRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            Status = JobStatus.Queued,
            TotalSteps = 1,
            CurrentStep = queuedStep,
            Percent = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        control.Jobs.Add(job);
        await control.SaveChangesAsync(cancellationToken);
        await notifier.JobChangedAsync(job, cancellationToken);
        return job.Id;
    }

    public async Task AdvanceAsync(Guid jobId, int step, int total, string name, CancellationToken cancellationToken)
    {
        var job = await control.Jobs.FirstAsync(item => item.Id == jobId, cancellationToken);
        job.Status = JobStatus.Running;
        job.Step = step;
        job.TotalSteps = total;
        job.CurrentStep = name;
        job.Percent = total == 0 ? 0 : step * 100 / total;
        job.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        await notifier.JobChangedAsync(job, cancellationToken);
    }

    public Task FinishAsync(Guid jobId, bool succeeded, string? error, CancellationToken cancellationToken) =>
        FinishAsync(jobId, succeeded, error, succeeded ? "Collection finished" : "Collection failed", cancellationToken);

    public async Task FinishAsync(Guid jobId, bool succeeded, string? error, string step, CancellationToken cancellationToken)
    {
        var job = await control.Jobs.FirstAsync(item => item.Id == jobId, cancellationToken);
        job.Status = succeeded ? JobStatus.Succeeded : JobStatus.Failed;
        job.Percent = succeeded ? 100 : job.Percent;
        job.Error = error;
        job.CurrentStep = step;
        job.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        await notifier.JobChangedAsync(job, cancellationToken);
    }
}

public sealed class CollectionRunner(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    ProviderSet providers,
    EvidenceWriter evidence,
    CollectionJobs jobs,
    TimeProvider time,
    ILogger<CollectionRunner> logger)
{
    public async Task RunFreshAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var jobId = await jobs.CreateAsync(tenantId, cancellationToken);
        await RunAsync(tenantId, jobId, cancellationToken);
    }

    public async Task RunAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(tenantId, jobId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Collection failed for tenant {TenantId}. {ExceptionType}", tenantId, exception.GetType().Name);
            await jobs.FinishAsync(jobId, false, "collection_failed", cancellationToken);
        }
    }

    private async Task RunCoreAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken)
    {
        var tenant = await control.Tenants.FirstAsync(item => item.Id == tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName) || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database name failed the safety check.");
        }

        await using var db = databases.Create(tenant.DatabaseName);
        var connections = await db.Integrations.Where(item => item.Status == "connected").ToListAsync(cancellationToken);
        var total = Math.Max(1, connections.Count + AutomatedChecks.All.Count);
        var step = 0;
        var failed = false;
        foreach (var connection in connections)
        {
            step++;
            await jobs.AdvanceAsync(jobId, step, total, "Collecting " + IntegrationPolicies.DisplayName(connection.Provider), cancellationToken);
            var collected = await providers.CollectAsync(connection, cancellationToken);
            if (!collected.IsSuccess || collected.Value is null)
            {
                connection.LastError = collected.Error ?? "collect_failed";
                failed = true;
                continue;
            }

            connection.LastError = null;
            connection.LastCollectedAt = time.GetUtcNow();
            await ReplaceAsync(db, connection.Provider, collected.Value.Resources, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        var resources = (await db.Resources.AsNoTracking().ToListAsync(cancellationToken)).Select(ProviderSet.ToResource).ToArray();
        var controls = await db.Controls.Include(item => item.Tests).ToListAsync(cancellationToken);
        var touched = new HashSet<Guid>();
        foreach (var spec in AutomatedChecks.All)
        {
            step++;
            await jobs.AdvanceAsync(jobId, step, total, spec.Name, cancellationToken);
            var result = AutomatedChecks.Evaluate(spec.Key, resources);
            var controlItem = controls.FirstOrDefault(item => item.Code == spec.ControlCode);
            if (controlItem is null)
            {
                continue;
            }

            Upsert(db, controlItem, spec, result);
            if (result.Outcome == TestOutcome.NotRun)
            {
                continue;
            }

            touched.Add(controlItem.Id);
            var body = await EvidenceWriter.ParquetAsync(AutomatedChecks.Inputs(spec.Key, resources), cancellationToken);
            var evidenceId = Guid.NewGuid();
            var item = new EvidenceItem
            {
                Id = evidenceId,
                ControlId = controlItem.Id,
                Title = spec.Name,
                Source = "Automated",
                ObjectKey = tenantId.ToString("N") + "/" + evidenceId.ToString("N") + ".parquet",
                Integrity = EvidenceIntegrity.NotStored,
            };
            db.Evidence.Add(item);
            await evidence.AppendAsync(
                db,
                tenantId,
                item,
                body,
                "application/vnd.apache.parquet",
                time.GetUtcNow().AddYears(tenant.RetentionYears),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var controlItem in controls.Where(item => touched.Contains(item.Id)))
        {
            controlItem.Status = Recompute(controlItem);
        }

        await db.SaveChangesAsync(cancellationToken);
        await jobs.FinishAsync(jobId, !failed, failed ? "collection_incomplete" : null, cancellationToken);
    }

    private async Task ReplaceAsync(
        TenantDbContext db,
        string provider,
        IReadOnlyList<NormalizedResource> resources,
        CancellationToken cancellationToken)
    {
        var existing = await db.Resources.Where(item => item.Provider == provider).ToListAsync(cancellationToken);
        db.Resources.RemoveRange(existing);
        await db.SaveChangesAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var resource in resources)
        {
            db.Resources.Add(new ResourceSnapshot
            {
                Id = Guid.NewGuid(),
                Provider = resource.Provider,
                Kind = resource.Kind,
                ExternalId = resource.ExternalId,
                Name = resource.Name,
                AttributesJson = JsonSerializer.Serialize(resource.Attributes),
                CollectedAt = now,
            });
        }
    }

    private static void Upsert(TenantDbContext db, Control controlItem, CheckSpec spec, CheckResult result)
    {
        var test = controlItem.Tests.FirstOrDefault(item => item.TestKey == spec.Key);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                TestKey = spec.Key,
            };
            controlItem.Tests.Add(test);
            db.ControlTests.Add(test);
        }

        test.Name = spec.Name;
        test.Kind = TestKind.Automated;
        test.Outcome = result.Outcome;
        test.Detail = result.Detail.Length > 2000 ? result.Detail[..2000] : result.Detail;
        test.Remediation = spec.Remediation;
        test.Requirements = string.Join(", ", spec.Requirements);
    }

    private static ControlStatus Recompute(Control controlItem)
    {
        var relevant = controlItem.Tests.Where(test => test.Outcome != TestOutcome.NotRun).ToArray();
        if (relevant.Any(test => test.Outcome == TestOutcome.Fail))
        {
            return ControlStatus.Failing;
        }

        if (relevant.Any(test => test.Outcome == TestOutcome.NeedsEvidence))
        {
            return ControlStatus.NeedsEvidence;
        }

        if (relevant.Any(test => test.Outcome == TestOutcome.Pass))
        {
            return ControlStatus.Passing;
        }

        return controlItem.Status;
    }
}
