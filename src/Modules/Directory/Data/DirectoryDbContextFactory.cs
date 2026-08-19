using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Directory.Data;

/// <summary>Design-time only — see the note on the platform factory. Scaffolding needs a provider, not a server.</summary>
public sealed class DirectoryDbContextFactory : IDesignTimeDbContextFactory<DirectoryDbContext>
{
    public DirectoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DirectoryDbContext.SchemaName))
            .Options;

        return new DirectoryDbContext(options);
    }
}
