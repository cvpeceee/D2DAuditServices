using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Catalog;
using D2DAudit.Infrastructure.Identity;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Queries;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddDbContext<ControlPlaneDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHasher<UserAccount>>();
        services.AddSingleton<IFrameworkCatalog, FrameworkCatalog>();
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantDatabases>();
        services.TryAddSingleton<IJobNotifier, NullJobNotifier>();
        services.AddScoped<IActivityWriter, ActivityWriter>();
        services.AddScoped<ITenantProvisioner, TenantProvisioner>();
        services.AddScoped<IDemoSeeder, DemoSeeder>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWorkspaceQuery, WorkspaceQuery>();
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
    IServiceProvider services,
    IHostEnvironment environment,
    IConfiguration configuration,
    Microsoft.Extensions.Logging.ILogger<StartupInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
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
