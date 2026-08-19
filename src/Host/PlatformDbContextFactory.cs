using Cracra.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Host;

/// <summary>
/// Used only by <c>dotnet ef</c>. Migrations are generated against a placeholder connection string because
/// scaffolding needs a provider, not a server — the real connection is assembled at runtime from the orchestrator's
/// configuration, and nothing about generating a migration should require a live database.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.SchemaName))
            .Options;

        return new PlatformDbContext(options);
    }
}
