using Cracra.BuildingBlocks.Persistence.Naming;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Cracra.BuildingBlocks.Persistence;

/// <summary>
/// Base for every module's DbContext. One Postgres schema per module (<c>architecture.md §3</c>), each with its
/// own outbox, and no foreign key ever crossing a schema boundary — cross-module references store the id and are
/// validated in the application layer.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    /// <summary>The Postgres schema this module owns, e.g. <c>directory</c> or <c>projects</c>.</summary>
    public string Schema { get; } = schema;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);

        // Applied last so it also renames anything a derived module configured above.
        modelBuilder.UseSnakeCaseNames();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // conventions.md §4 / architecture.md §5: every instant is timestamptz, stored UTC. Postgres maps
        // DateTimeOffset to timestamptz natively, so this is about refusing the alternatives rather than mapping.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");

        base.ConfigureConventions(configurationBuilder);
    }
}
