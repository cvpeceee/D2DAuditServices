using D2DAudit.Domain;
using Microsoft.AspNetCore.Hosting;
using Npgsql;

namespace D2DAudit.Tests;

public sealed class ApiFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    private readonly string _adminConnection;
    private readonly string _environment;

    public string DatabaseName { get; } = "cp_" + Guid.NewGuid().ToString("N");

    public ApiFactory(string environment = "Testing")
    {
        _environment = environment;
        _adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres";
        if (!DatabaseNames.IsSafe(DatabaseName))
        {
            throw new InvalidOperationException("Test database name failed the safety check.");
        }

        Execute(_adminConnection, $"CREATE DATABASE {DatabaseName}");
    }

    public string ControlConnection
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = DatabaseName };
            return builder.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:ControlPlane", ControlConnection);
        builder.UseSetting(WebHostDefaults.EnvironmentKey, _environment);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        catch (Exception)
        {
            // The host can fail during a negative test. Databases are still dropped below.
        }

        var tenantDatabases = new List<string>();
        try
        {
            await using var connection = new NpgsqlConnection(ControlConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT \"DatabaseName\" FROM tenants", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    tenantDatabases.Add(reader.GetString(0));
                }
            }
        }
        catch (PostgresException)
        {
            // The control database may not have been migrated when startup was rejected.
        }

        foreach (var name in tenantDatabases.Where(DatabaseNames.IsSafe))
        {
            Drop(name);
        }

        Drop(DatabaseName);
    }

    private void Drop(string name)
    {
        if (!DatabaseNames.IsSafe(name))
        {
            return;
        }

        Execute(_adminConnection, $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}' AND pid <> pg_backend_pid()");
        Execute(_adminConnection, $"DROP DATABASE IF EXISTS {name}");
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
