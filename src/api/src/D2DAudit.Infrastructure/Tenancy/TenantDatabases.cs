using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace D2DAudit.Infrastructure.Tenancy;

public sealed class TenantDatabases(IConfiguration configuration)
{
    public TenantDbContext Create(string databaseName)
    {
        if (!DatabaseNames.IsSafe(databaseName))
        {
            throw new InvalidOperationException("Refusing to open a database whose name failed the safety check.");
        }

        var controlPlane = configuration.GetConnectionString("ControlPlane")
            ?? throw new InvalidOperationException("ConnectionStrings:ControlPlane is required.");
        var settings = DatabaseSettings.From(configuration);
        var connectionString = settings.TenantConnectionString(controlPlane, databaseName);
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString, settings.Apply).Options;
        return new TenantDbContext(options);
    }

    public string AdminConnectionString()
    {
        var controlPlane = configuration.GetConnectionString("ControlPlane")
            ?? throw new InvalidOperationException("ConnectionStrings:ControlPlane is required.");
        return new NpgsqlConnectionStringBuilder(controlPlane).ConnectionString;
    }
}
