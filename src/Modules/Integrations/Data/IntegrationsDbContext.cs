using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Integrations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Integrations.Data;

public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "integrations";

    public DbSet<ExternalConnection> Connections => Set<ExternalConnection>();

    public DbSet<ExternalMapping> Mappings => Set<ExternalMapping>();

    public DbSet<ExternalWorkItem> WorkItems => Set<ExternalWorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ExternalConnectionConfiguration : IEntityTypeConfiguration<ExternalConnection>
{
    public void Configure(EntityTypeBuilder<ExternalConnection> builder)
    {
        builder.ToTable("external_connection");
        builder.HasKey(connection => connection.Id);

        builder.Property(connection => connection.Provider).HasMaxLength(64).IsRequired();
        builder.Property(connection => connection.Name).HasMaxLength(256).IsRequired();
        builder.Property(connection => connection.BaseUrl).HasMaxLength(1024).IsRequired();
        builder.Property(connection => connection.ProjectOrQueue).HasMaxLength(256).IsRequired();
        builder.Property(connection => connection.CurrentSprint).HasMaxLength(256);
        builder.Property(connection => connection.LastSyncStatus).HasMaxLength(32).IsRequired();

        // The provider's message, kept short on purpose: this is a hint for whoever configured the connection,
        // not a diagnostic. The diagnostic is the correlated log in SEQ, and a 4 KB stack trace on a config row
        // is how a screen ends up rendering somebody else's infrastructure topology.
        builder.Property(connection => connection.LastSyncError).HasMaxLength(1024);

        // A name, not a secret. Bounded like a key because that is what it is.
        builder.Property(connection => connection.AuthRef).HasMaxLength(128).IsRequired();

        // The scheduler's own question — "what is active and due" — and the administration list's, which is
        // everything in a department.
        builder.HasIndex(connection => new { connection.DepartmentId, connection.Provider });
        builder.HasIndex(connection => connection.Active);

        builder.HasMany(connection => connection.Mappings)
            .WithOne(mapping => mapping.Connection)
            .HasForeignKey(mapping => mapping.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExternalMappingConfiguration : IEntityTypeConfiguration<ExternalMapping>
{
    public void Configure(EntityTypeBuilder<ExternalMapping> builder)
    {
        builder.ToTable("external_mapping", table => table.HasCheckConstraint(
            "ck_external_mapping_target",
            // The discriminator's other half. Without it the one-table-with-a-kind decision would be a naming
            // convention rather than a constraint, and an area path pointing at a unit would mirror items into a
            // queue nobody expects to see them in.
            """
            (kind in ('area-path', 'iteration') and project_id is not null and unit_id is null)
            or (kind = 'assignment-group' and unit_id is not null and project_id is null)
            """));

        builder.HasKey(mapping => mapping.Id);

        builder.Property(mapping => mapping.Kind).HasMaxLength(32).IsRequired();
        builder.Property(mapping => mapping.ExternalValue).HasMaxLength(512).IsRequired();

        // The lookup the synchronizer runs per item: "this connection, this kind, this value". Unique because
        // two rows mapping one area path to two projects has no defensible answer.
        builder.HasIndex(mapping => new { mapping.ConnectionId, mapping.Kind, mapping.ExternalValue }).IsUnique();
        builder.HasIndex(mapping => mapping.DepartmentId);
    }
}

internal sealed class ExternalWorkItemConfiguration : IEntityTypeConfiguration<ExternalWorkItem>
{
    public void Configure(EntityTypeBuilder<ExternalWorkItem> builder)
    {
        builder.ToTable("external_work_item");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Provider).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Reference).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.Type).HasMaxLength(64).IsRequired();
        builder.Property(item => item.State).HasMaxLength(64).IsRequired();
        builder.Property(item => item.AssignedToLdapUid).HasMaxLength(128);
        builder.Property(item => item.SprintOrQueue).HasMaxLength(512);
        builder.Property(item => item.Url).HasMaxLength(2048);
        builder.Property(item => item.MirrorState).HasMaxLength(32).IsRequired();
        builder.Property(item => item.EstimatedHours).HasPrecision(6, 2);

        // The upsert key, and the reason a pull run twice changes nothing the first one did not.
        //
        // Scoped to the connection rather than to the provider, because an external id is only unique inside the
        // system that issued it: work item 4301 exists in every DevOps collection there has ever been, and a
        // provider-wide unique index would make the second collection an organization connects fail on insert
        // with a constraint violation nobody could read.
        //
        // The cost is that two connections covering the same project mirror its items twice. That is a
        // configuration mistake with a visible symptom — the same task listed twice in the dropdown — and it is
        // the right place for it to surface, rather than as a pull that dies halfway.
        builder.HasIndex(item => new { item.ConnectionId, item.ExternalId }).IsUnique();

        // The feeds' shapes. S5 asks "mine, open"; S6a asks "this unit's queue, open, unassigned". Both lead on
        // the state because closed items are the growing part of this table and neither feed ever wants them.
        builder.HasIndex(item => new { item.MirrorState, item.AssignedPersonId });
        builder.HasIndex(item => new { item.MirrorState, item.UnitId, item.AssignedPersonId });
        builder.HasIndex(item => new { item.MirrorState, item.ProjectId, item.IsCurrentSprint });

        // No separate index on connection_id: the unique one above already leads on it, so the synchronizer's
        // "everything this connection mirrors" scan is served by it.

        // Cascading, and a real foreign key rather than a bare id column. A mirror row is a cache of somebody
        // else's data that means nothing without the connection explaining where it came from, so removing the
        // connection must remove them — and leaving them behind would also keep them in the unique index, so
        // reconfiguring the same integration would fail on insert against rows nobody could see.
        //
        // No navigation property on the entity: the synchronizer already holds the connection it is reconciling,
        // and an unused navigation is one more way for a query to accidentally load one.
        builder.HasOne<ExternalConnection>()
            .WithMany()
            .HasForeignKey(item => item.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
