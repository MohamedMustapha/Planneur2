using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Projects.Infrastructure;

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class ProjectsDbContextFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ProjectsDbContext.SchemaName))
            .Options;

        return new ProjectsDbContext(options);
    }
}
