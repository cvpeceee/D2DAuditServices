using D2DAudit.Application;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

public static class ConnectorServices
{
    /// <summary>Registers every live adapter in this assembly plus the registry, executor and hub.</summary>
    public static IServiceCollection AddConnectorCatalog(this IServiceCollection services)
    {
        services.AddHttpClient(LiveConnector.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = 32 * 1024 * 1024;
        });
        var adapters = typeof(LiveConnector).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && type.IsSubclassOf(typeof(LiveConnector)));
        foreach (var adapter in adapters)
        {
            services.AddSingleton(typeof(LiveConnector), adapter);
        }

        services.AddSingleton<ConnectorRegistry>();
        services.AddSingleton<IConnectorRegistry>(provider => provider.GetRequiredService<ConnectorRegistry>());
        services.AddSingleton<ConnectorExecutor>();
        services.AddScoped<IConnectorHub, ConnectorHub>();
        return services;
    }
}
