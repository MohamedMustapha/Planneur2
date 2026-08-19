using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Access.Data;

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class AccessDbContextFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<AccessDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AccessDbContext.SchemaName))
            .Options;

        return new AccessDbContext(options);
    }
}
