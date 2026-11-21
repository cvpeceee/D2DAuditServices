using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Changes;
using D2DAudit.Infrastructure.CloudSecurity;
using D2DAudit.Infrastructure.Continuity;
using D2DAudit.Infrastructure.Descriptions;
using D2DAudit.Infrastructure.Enterprise;
using D2DAudit.Infrastructure.GovernanceHub;
using D2DAudit.Infrastructure.Questionnaires;
using D2DAudit.Infrastructure.Governance;
using D2DAudit.Infrastructure.Catalog;
using D2DAudit.Infrastructure.Policies;
using D2DAudit.Infrastructure.Program;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Identity;
using D2DAudit.Infrastructure.Incidents;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Operations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Queries;
using D2DAudit.Infrastructure.Storage;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Common;
using Temporalio.Extensions.Hosting;
using Temporalio.Worker;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("ControlPlane");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:ControlPlane is required.");
        }

        var apiKey = First(configuration["WORKOS_API_KEY"], Environment.GetEnvironmentVariable("WORKOS_API_KEY"), configuration["WorkOS:ApiKey"]);
        var clientId = First(configuration["WORKOS_CLIENT_ID"], Environment.GetEnvironmentVariable("WORKOS_CLIENT_ID"), configuration["WorkOS:ClientId"]);
        var mode = IdentityModeResolver.Resolve(environment.EnvironmentName, apiKey, clientId);
        var temporalAddress = First(configuration["TEMPORAL_ADDRESS"], Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS"));
        if (environment.IsProduction() || environment.IsStaging())
        {
            if (string.IsNullOrWhiteSpace(temporalAddress))
            {
                throw new InvalidOperationException("Production requires TEMPORAL_ADDRESS for scheduled collection.");
            }

            var storageEndpoint = configuration["Storage:Endpoint"];
            var objectLock = string.Equals(configuration["Storage:ObjectLock"], "true", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(storageEndpoint) || !objectLock)
            {
                throw new InvalidOperationException("Production requires Storage:Endpoint and Storage:ObjectLock=true.");
            }
        }

        services.AddDbContext<ControlPlaneDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHasher<UserAccount>>();
        services.AddSingleton<LocalDummyPasswordHash>();
        services.AddSingleton<IFrameworkCatalog, FrameworkCatalog>();
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantDatabases>();
        services.TryAddSingleton<IJobNotifier, NullJobNotifier>();
        services.AddScoped<IActivityWriter, ActivityWriter>();
        services.AddValidatorsFromAssemblyContaining<CreateTenantValidator>();
        services.AddScoped<IControlPlaneDirectory, ControlPlaneDirectory>();
        services.AddScoped<ITenantProvisioner, TenantProvisioner>();
        services.AddScoped<IDemoSeeder, DemoSeeder>();
        var redis = First(configuration["Redis:ConnectionString"], Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"));
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "d2d:";
            });
        }

        services.TryAddSingleton<ISessionGuard, DistributedSessionGuard>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWorkspaceQuery, WorkspaceQuery>();
        services.AddScoped<IClientRead, ClientPortal>();
        services.AddSingleton<SecretBox>();
        services.AddSingleton<EvidenceWriter>();
        if (string.IsNullOrWhiteSpace(configuration["Storage:Endpoint"]))
        {
            services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        }
        else
        {
            services.AddSingleton<IStorageProvider, S3StorageProvider>();
        }

        services.AddSingleton<AwsCloudProvider>();
        services.AddHttpClient<AzureCloudProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<GcpCloudProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<ICloudProvider>(provider => provider.GetRequiredService<AwsCloudProvider>());
        services.AddScoped<ICloudProvider>(provider => provider.GetRequiredService<AzureCloudProvider>());
        services.AddScoped<ICloudProvider>(provider => provider.GetRequiredService<GcpCloudProvider>());
        services.AddHttpClient<GoogleWorkspaceDirectory>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<Microsoft365Directory>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<GitHubSourceControl>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IWorkspaceDirectory>(provider => provider.GetRequiredService<GoogleWorkspaceDirectory>());
        services.AddScoped<IWorkspaceDirectory>(provider => provider.GetRequiredService<Microsoft365Directory>());
        services.AddScoped<ISourceControlProvider>(provider => provider.GetRequiredService<GitHubSourceControl>());
        services.AddScoped<ProviderSet>();
        services.AddScoped<CollectionJobs>();
        services.AddScoped<CollectionRunner>();
        services.AddScoped<IIntegrationQuery, IntegrationQuery>();
        services.AddScoped<IPolicyPortal, PolicyPortal>();
        services.AddHttpClient<IIssueTracker, ConfigurableIssueTracker>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<INotifier, ConfigurableNotifier>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IAutomationDispatcher, AutomationDispatcher>();
        services.AddScoped<IGovernance, GovernancePortal>();
        services.AddScoped<IOperations, OperationsPortal>();
        services.AddScoped<IIncidents, IncidentPortal>();
        services.AddHttpClient<JiraChangeFeed>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IJiraChangeFeed>(provider => provider.GetRequiredService<JiraChangeFeed>());
        services.AddScoped<IChanges, ChangePortal>();
        services.AddScoped<IQuestionnaireAssistant, GroundedQuestionnaireAssistant>();
        services.AddScoped<IQuestionnaires, QuestionnairePortal>();
        services.AddScoped<ISystemDescription, SystemDescriptionPortal>();
        services.AddScoped<IContinuity, ContinuityPortal>();
        services.AddScoped<ICloudSecurity, CloudSecurityPortal>();
        services.AddHttpClient<IAssistantProvider, ConfigurableAssistantProvider>(client => client.Timeout = TimeSpan.FromSeconds(25));
        services.AddSingleton<WebhookSecretProtector>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddHttpClient<IWebhookDispatcher, WebhookDispatcher>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IEnterprisePortal, EnterprisePortal>();
        services.AddHttpClient<IGovernanceHub, GovernanceHubPortal>(client => client.Timeout = TimeSpan.FromSeconds(25));
        services.AddSingleton<AwsInspectorFeed>();
        services.AddScoped<IVulnerabilityIngest, VulnerabilityIngest>();
        services.AddHttpClient<MdmImport>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IMdmImport>(provider => provider.GetRequiredService<MdmImport>());
        services.AddScoped<IDeviceFleet, DeviceFleet>();
        services.AddScoped<IProgram, ProgramPortal>();
        services.AddScoped<IAuditPortal, AuditPortal>();
        services.AddScoped<AuditPackageRunner>();
        services.AddSingleton<AuditExportQueue>();
        services.AddHostedService(provider => provider.GetRequiredService<AuditExportQueue>());
        if (string.IsNullOrWhiteSpace(temporalAddress))
        {
            services.AddSingleton<LocalCollectionSchedule>();
            services.AddSingleton<ICollectionSchedule>(provider => provider.GetRequiredService<LocalCollectionSchedule>());
            services.AddHostedService(provider => provider.GetRequiredService<LocalCollectionSchedule>());
        }
        else
        {
            services.AddTemporalClient(temporalAddress, "default");
            services.AddHostedTemporalWorker(
                    temporalAddress,
                    "default",
                    TemporalCollectionSchedule.TaskQueue,
                    new WorkerDeploymentOptions(new WorkerDeploymentVersion("d2d", "1"), false))
                .AddWorkflow<CollectionWorkflow>()
                .AddWorkflow<DailyCollectionWorkflow>()
                .AddScopedActivities<CollectionActivities>();
            services.AddSingleton<TemporalCollectionSchedule>();
            services.AddSingleton<ICollectionSchedule>(provider => provider.GetRequiredService<TemporalCollectionSchedule>());
            services.AddHostedService(provider => provider.GetRequiredService<TemporalCollectionSchedule>());
        }
        services.Configure<WorkOsOptions>(options =>
        {
            options.ApiKey = apiKey ?? "";
            options.ClientId = clientId ?? "";
            options.Issuer = First(Environment.GetEnvironmentVariable("WORKOS_ISSUER"), configuration["WorkOS:Issuer"]) ?? "https://api.workos.com";
            options.Audience = First(Environment.GetEnvironmentVariable("WORKOS_TOKEN_AUDIENCE"), configuration["WorkOS:Audience"]);
            options.RedirectUris = configuration.GetSection("WorkOS:RedirectUris").Get<string[]>() ?? [];
        });

        if (mode == IdentityMode.Local)
        {
            services.AddScoped<IIdentityProvider, LocalIdentityProvider>();
        }
        else
        {
            services.AddHttpClient<IIdentityProvider, WorkOsIdentityProvider>(client => client.Timeout = TimeSpan.FromSeconds(20));
        }

        services.AddHostedService<StartupInitializer>();
        return services;
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class NullJobNotifier : IJobNotifier
{
    public Task JobChangedAsync(JobRecord job, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ActivityWriter(ControlPlaneDbContext db, TimeProvider time) : IActivityWriter
{
    public async Task WriteAsync(
        Guid? actorUserId,
        Guid? tenantId,
        string action,
        string result,
        string? detail,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        db.Activity.Add(new ActivityEvent
        {
            Id = Guid.NewGuid(),
            At = time.GetUtcNow(),
            ActorUserId = actorUserId,
            TenantId = tenantId,
            Action = action,
            Result = result,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail,
            IpAddress = ipAddress is { Length: > 64 } ? ipAddress[..64] : ipAddress,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class StartupInitializer(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IConfiguration configuration,
    Microsoft.Extensions.Logging.ILogger<StartupInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        await control.Database.MigrateAsync(cancellationToken);
        if (environment.IsDevelopment() && configuration.GetValue("Seed:Demo", true))
        {
            logger.LogInformation("Seeding the example workspace. This data is not a customer tenant.");
            await scope.ServiceProvider.GetRequiredService<IDemoSeeder>().SeedAsync(cancellationToken);
        }

        var ids = await control.Tenants.Select(tenant => tenant.Id).ToListAsync(cancellationToken);
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();
        foreach (var id in ids)
        {
            await provisioner.MigrateExistingAsync(id, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
