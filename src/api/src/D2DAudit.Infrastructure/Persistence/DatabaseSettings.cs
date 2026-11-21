using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace D2DAudit.Infrastructure.Persistence;

public sealed record DatabaseSettings(int CommandTimeoutSeconds, int MaxRetryCount, int TenantMaxPoolSize)
{
    public static DatabaseSettings From(IConfiguration configuration) => new(
        Read(configuration, "Database:CommandTimeoutSeconds", 30, 1, 600),
        Read(configuration, "Database:MaxRetryCount", 3, 0, 10),
        Read(configuration, "Database:TenantMaxPoolSize", 20, 1, 200));

    public void Apply(NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.CommandTimeout(CommandTimeoutSeconds);
        if (MaxRetryCount > 0)
        {
            npgsql.EnableRetryOnFailure(MaxRetryCount);
        }
    }

    // Each tenant database gets its own Npgsql pool; cap it so many tenants cannot exhaust max_connections.
    public string TenantConnectionString(string controlPlaneConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(controlPlaneConnectionString) { Database = databaseName };
        if (builder.MaxPoolSize > TenantMaxPoolSize)
        {
            builder.MaxPoolSize = TenantMaxPoolSize;
        }

        if (builder.MinPoolSize > builder.MaxPoolSize)
        {
            builder.MinPoolSize = 0;
        }

        return builder.ConnectionString;
    }

    private static int Read(IConfiguration configuration, string key, int fallback, int min, int max) =>
        int.TryParse(configuration[key], out var value) ? Math.Clamp(value, min, max) : fallback;
}
