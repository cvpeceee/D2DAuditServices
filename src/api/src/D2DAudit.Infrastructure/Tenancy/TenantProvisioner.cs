using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace D2DAudit.Infrastructure.Tenancy;

public sealed class TenantProvisioner(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    IFrameworkCatalog catalog,
    IValidator<CreateTenantRequest> tenants,
    IJobNotifier jobs,
    TimeProvider time,
    ILogger<TenantProvisioner> logger) : ITenantProvisioner
{
    public const int SchemaVersion = 2;

    public async Task<Result<Tenant>> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var validation = await tenants.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<Tenant>.Fail("invalid_tenant");
        }

        var existing = await control.Tenants.FirstOrDefaultAsync(tenant => tenant.Slug == request.Slug, cancellationToken);
        if (existing is not null)
        {
            await EnsureReadyAsync(existing, cancellationToken);
            return Result<Tenant>.Ok(existing);
        }

        var id = Guid.NewGuid();
        var now = time.GetUtcNow();
        var tenant = new Tenant
        {
            Id = id,
            Slug = request.Slug,
            Name = request.Name.Trim(),
            Region = request.Region,
            Status = TenantStatus.Provisioning,
            DatabaseName = DatabaseNames.ForTenant(id),
            RetentionYears = DatabaseNames.MinimumRetentionYears,
            IsExample = request.IsExample,
            CreatedAt = now,
        };
        control.Tenants.Add(tenant);
        await control.SaveChangesAsync(cancellationToken);
        await EnsureReadyAsync(tenant, cancellationToken);
        return Result<Tenant>.Ok(tenant);
    }

    public async Task MigrateExistingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await control.Tenants.FirstAsync(item => item.Id == tenantId, cancellationToken);
        await EnsureReadyAsync(tenant, cancellationToken);
    }

    public async Task SeedExampleControlsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await control.Tenants.FirstAsync(item => item.Id == tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not provisioned.");
        }

        await using var db = databases.Create(tenant.DatabaseName);
        var present = await db.Controls.Select(controlItem => controlItem.Code).ToListAsync(cancellationToken);
        var have = present.ToHashSet(StringComparer.Ordinal);
        var requirements = await db.Requirements.Include(requirement => requirement.Framework).ToListAsync(cancellationToken);
        var byKey = requirements.ToDictionary(requirement => (requirement.Framework!.Key, requirement.Code));
        foreach (var item in catalog.ExampleControls.Controls)
        {
            if (have.Contains(item.Code))
            {
                continue;
            }

            var entity = new Control
            {
                Id = Guid.NewGuid(),
                Code = item.Code,
                Title = item.Title,
                Description = item.Description,
                Status = Enum.Parse<ControlStatus>(item.Status, ignoreCase: true),
                Frequency = item.Frequency,
                OwnerName = item.OwnerName,
                OwnerEmail = item.OwnerEmail,
            };
            db.Controls.Add(entity);
            foreach (var mapping in item.Mappings)
            {
                if (!byKey.TryGetValue((mapping.Framework, mapping.Requirement), out var requirement))
                {
                    throw new InvalidOperationException(
                        $"Example control {item.Code} maps to unknown requirement {mapping.Framework} {mapping.Requirement}.");
                }

                db.ControlMappings.Add(new ControlRequirementMap
                {
                    ControlId = entity.Id,
                    RequirementId = requirement.Id,
                    Coverage = Enum.Parse<MappingCoverage>(mapping.Coverage, ignoreCase: true),
                });
            }

            foreach (var test in item.Tests)
            {
                db.ControlTests.Add(new ControlTest
                {
                    Id = Guid.NewGuid(),
                    ControlId = entity.Id,
                    Name = test.Name,
                    Kind = Enum.Parse<TestKind>(test.Kind, ignoreCase: true),
                    Outcome = Enum.Parse<TestOutcome>(test.Outcome, ignoreCase: true),
                    Detail = test.Detail,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureReadyAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName) || !DatabaseNames.IsSafe(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database name failed the safety check.");
        }

        var track = tenant.Status != TenantStatus.Active;
        JobRecord? job = null;
        if (track)
        {
            var now = time.GetUtcNow();
            job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Kind = "tenant_provision",
                Status = JobStatus.Queued,
                TotalSteps = 4,
                CurrentStep = "Queued",
                Percent = 0,
                CreatedAt = now,
                UpdatedAt = now,
            };
            control.Jobs.Add(job);
            await control.SaveChangesAsync(cancellationToken);
            await jobs.JobChangedAsync(job, cancellationToken);
        }

        try
        {
            if (job is not null)
            {
                await AdvanceAsync(job, 1, "Creating the tenant database", cancellationToken);
            }

            await EnsureDatabaseAsync(tenant.DatabaseName, cancellationToken);
            if (job is not null)
            {
                await AdvanceAsync(job, 2, "Applying database migrations", cancellationToken);
            }

            await using var db = databases.Create(tenant.DatabaseName);
            await db.Database.MigrateAsync(cancellationToken);
            if (job is not null)
            {
                await AdvanceAsync(job, 3, "Loading frameworks", cancellationToken);
            }

            await LoadFrameworksAsync(db, cancellationToken);
            tenant.Status = TenantStatus.Active;
            tenant.SchemaVersion = SchemaVersion;
            if (job is not null)
            {
                job.Status = JobStatus.Succeeded;
                job.Step = 4;
                job.TotalSteps = 4;
                job.Percent = 100;
                job.CurrentStep = "Tenant is ready";
                job.Error = null;
                job.UpdatedAt = time.GetUtcNow();
            }

            await control.SaveChangesAsync(cancellationToken);
            if (job is not null)
            {
                await jobs.JobChangedAsync(job, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Tenant provisioning failed for {TenantId}. {ExceptionType}",
                tenant.Id,
                exception.GetType().Name);
            if (job is not null)
            {
                job.Status = JobStatus.Failed;
                job.Error = "Tenant provisioning failed.";
                job.UpdatedAt = time.GetUtcNow();
                await control.SaveChangesAsync(cancellationToken);
                await jobs.JobChangedAsync(job, cancellationToken);
            }

            throw;
        }
    }

    private async Task AdvanceAsync(JobRecord job, int step, string name, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Running;
        job.Step = step;
        job.CurrentStep = name;
        job.Percent = (step - 1) * 100 / job.TotalSteps;
        job.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        await jobs.JobChangedAsync(job, cancellationToken);
    }

    private async Task EnsureDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(databases.AdminConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", databaseName);
        if (await exists.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return;
        }

        await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task LoadFrameworksAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Frameworks.AnyAsync(cancellationToken))
        {
            return;
        }

        var sort = 0;
        void AddNodes(IReadOnlyList<RequirementNode> nodes, Guid frameworkId, Guid? parentId)
        {
            foreach (var node in nodes)
            {
                var requirement = new Requirement
                {
                    Id = Guid.NewGuid(),
                    FrameworkId = frameworkId,
                    ParentId = parentId,
                    Code = node.Code,
                    Title = node.Title,
                    Summary = node.Summary,
                    SortOrder = sort++,
                };
                db.Requirements.Add(requirement);
                AddNodes(node.Children, frameworkId, requirement.Id);
            }
        }

        foreach (var document in catalog.Frameworks)
        {
            var framework = new FrameworkRecord
            {
                Id = Guid.NewGuid(),
                Key = document.Key,
                Name = document.Name,
                Version = document.Version,
                Description = document.Description,
            };
            db.Frameworks.Add(framework);
            AddNodes(document.Requirements, framework.Id, null);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
