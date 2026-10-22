using Testcontainers.PostgreSql;

namespace D2DAudit.Tests;

public sealed class PostgresServer : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _container;

    private PostgresServer(string connectionString, PostgreSqlContainer? container)
    {
        ConnectionString = connectionString;
        _container = container;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresServer> StartAsync()
    {
        var existing = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new PostgresServer(existing, null);
        }

        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithUsername("d2d")
            .WithPassword("d2d_dev_password")
            .Build();
        await container.StartAsync();
        return new PostgresServer(container.GetConnectionString(), container);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
