using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Meetings.Data;

/// <summary>Design-time only — see the note on the platform factory. Scaffolding needs a provider, not a server.</summary>
public sealed class MeetingsDbContextFactory : IDesignTimeDbContextFactory<MeetingsDbContext>
{
    public MeetingsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<MeetingsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MeetingsDbContext.SchemaName))
            .Options;

        return new MeetingsDbContext(options);
    }
}
