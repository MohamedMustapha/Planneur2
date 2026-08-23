using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Contracts = Cracra.Modules.Portfolio.Contracts;

namespace Cracra.Modules.Portfolio.Infrastructure;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "portfolio";

    public DbSet<PortfolioItem> Items => Set<PortfolioItem>();

    public DbSet<Iteration> Iterations => Set<Iteration>();

    public DbSet<PortfolioTransition> Transitions => Set<PortfolioTransition>();

    public DbSet<Epic> Epics => Set<Epic>();

    public DbSet<ItemDependency> Dependencies => Set<ItemDependency>();

    public DbSet<ItemMember> Members => Set<ItemMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PortfolioDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        PortfolioOutboxDispatcher.DispatchDomainEvents(this);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

internal sealed class PortfolioItemConfiguration : IEntityTypeConfiguration<PortfolioItem>
{
    public void Configure(EntityTypeBuilder<PortfolioItem> builder)
    {
        builder.ToTable("portfolio_item");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.DecisionNotes).HasMaxLength(4000);

        builder.Property(item => item.Code).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Classification).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.Category).HasMaxLength(64);
        builder.Property(item => item.Currency).HasMaxLength(3).IsRequired();
        builder.Property(item => item.AwaitingVersion).HasMaxLength(32);
        builder.Property(item => item.Summary).HasMaxLength(4000);
        builder.Property(item => item.EstimateAmount).HasPrecision(14, 2);

        builder.HasIndex(item => item.Code).IsUnique();
        builder.HasIndex(item => item.OwnerNodeId);
        builder.HasIndex(item => new { item.Type, item.Category });

        // RLS reads the department on every row, and the board's default view orders by priority within it.
        builder.HasIndex(item => item.DepartmentId);
        builder.HasIndex(item => new { item.State, item.Priority });

        // One project, one portfolio item. The commit handler checks this too, for a message better than a
        // constraint violation — but the constraint is what actually guarantees it under concurrency.
        builder.HasIndex(item => item.ProjectId)
            .IsUnique()
            .HasFilter("project_id is not null");

        builder.Ignore(item => item.DomainEvents);
        builder.Ignore(item => item.CurrentIteration);
        builder.Ignore(item => item.IsArchived);

        builder.HasMany(item => item.Iterations)
            .WithOne()
            .HasForeignKey(iteration => iteration.PortfolioItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Epics)
            .WithOne()
            .HasForeignKey(epic => epic.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Members)
            .WithOne()
            .HasForeignKey(member => member.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Dependencies)
            .WithOne()
            .HasForeignKey(edge => edge.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(item => item.Iterations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.Epics).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.Dependencies).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EpicConfiguration : IEntityTypeConfiguration<Epic>
{
    public void Configure(EntityTypeBuilder<Epic> builder)
    {
        builder.ToTable("epic");
        builder.HasKey(epic => epic.Id);

        builder.Property(epic => epic.Id).ValueGeneratedNever();
        builder.Property(epic => epic.Name).HasMaxLength(256).IsRequired();
        builder.Property(epic => epic.Description).HasMaxLength(4000);
        builder.Property(epic => epic.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(epic => epic.TargetVersion).HasMaxLength(32);

        builder.Ignore(epic => epic.IsQueued);

        builder.HasIndex(epic => new { epic.ItemId, epic.Sequence });
        builder.HasIndex(epic => epic.IterationId);
    }
}

internal sealed class ItemDependencyConfiguration : IEntityTypeConfiguration<ItemDependency>
{
    public void Configure(EntityTypeBuilder<ItemDependency> builder)
    {
        builder.ToTable("item_dependency");
        builder.HasKey(edge => edge.Id);

        builder.Property(edge => edge.Id).ValueGeneratedNever();
        builder.Property(edge => edge.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(edge => edge.Note).HasMaxLength(1000);

        // One edge per pair. The aggregate refuses a duplicate for a readable message; this is what holds it
        // under two people adding the same dependency at once.
        builder.HasIndex(edge => new { edge.ItemId, edge.DependsOnItemId }).IsUnique();

        // The "consumed by" half of the catalog reads this end, and reads it for every platform card drawn.
        builder.HasIndex(edge => edge.DependsOnItemId);
    }
}

internal sealed class ItemMemberConfiguration : IEntityTypeConfiguration<ItemMember>
{
    public void Configure(EntityTypeBuilder<ItemMember> builder)
    {
        builder.ToTable("item_member");
        builder.HasKey(member => member.Id);

        builder.Property(member => member.Id).ValueGeneratedNever();

        builder.HasIndex(member => new { member.ItemId, member.PersonId });
        builder.HasIndex(member => member.PersonId);
        builder.HasIndex(member => member.NodeId);
    }
}

internal sealed class IterationConfiguration : IEntityTypeConfiguration<Iteration>
{
    public void Configure(EntityTypeBuilder<Iteration> builder)
    {
        builder.ToTable("iteration");
        builder.HasKey(iteration => iteration.Id);

        builder.Property(iteration => iteration.Id).ValueGeneratedNever();
        builder.Property(iteration => iteration.Name).HasMaxLength(128).IsRequired();
        builder.Property(iteration => iteration.Length).HasConversion<string>().HasMaxLength(16);
        builder.Property(iteration => iteration.State).HasConversion<string>().HasMaxLength(16);

        builder.Ignore(iteration => iteration.IsOpen);

        builder.HasIndex(iteration => new { iteration.PortfolioItemId, iteration.Sequence }).IsUnique();
    }
}

internal sealed class PortfolioTransitionConfiguration : IEntityTypeConfiguration<PortfolioTransition>
{
    public void Configure(EntityTypeBuilder<PortfolioTransition> builder)
    {
        builder.ToTable("portfolio_transition");
        builder.HasKey(transition => transition.Id);

        builder.Property(transition => transition.Id).ValueGeneratedNever();
        builder.Property(transition => transition.FromState).HasConversion<string>().HasMaxLength(16);
        builder.Property(transition => transition.ToState).HasConversion<string>().HasMaxLength(16);
        builder.Property(transition => transition.Reason).HasMaxLength(4000).IsRequired();

        // Denormalised from the item so the trail's RLS policy does not have to join back to a row that may itself
        // be filtered — and so the trail survives as evidence in its own right.
        builder.HasIndex(transition => transition.DepartmentId);
        builder.HasIndex(transition => new { transition.PortfolioItemId, transition.DecidedAt });
    }
}

/// <summary>
/// Turns domain events into outbox rows just before EF writes, in the same transaction as the change.
/// </summary>
internal static class PortfolioOutboxDispatcher
{
    public static void DispatchDomainEvents(PortfolioDbContext context)
    {
        var aggregates = context.ChangeTracker
            .Entries<PortfolioItem>()
            .Select(entry => entry.Entity)
            .Where(item => item.DomainEvents.Count > 0)
            .ToArray();

        foreach (var item in aggregates)
        {
            foreach (var domainEvent in item.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            item.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Only the events other modules act on cross the boundary. ItemConsidered and ItemReverted stay internal:
    /// nothing downstream changes behaviour because a candidate was registered, and publishing them would mean
    /// public contract that must stay compatible forever for no consumer.
    /// </summary>
    private static BuildingBlocks.Messaging.IIntegrationEvent? Translate(object domainEvent) => domainEvent switch
    {
        Domain.ItemCommitted committed => new Contracts.ItemCommitted(committed.ItemId, committed.ProjectId),

        Domain.ItemActivated activated => new Contracts.ItemActivated(activated.ItemId, activated.ProjectId),

        Domain.ItemArchived archived =>
            new Contracts.ItemArchived(archived.ItemId, archived.ProjectId, archived.Reason),

        Domain.IterationOpened opened =>
            new Contracts.IterationOpened(opened.ItemId, opened.IterationId, opened.StartsOn, opened.EndsOn),

        Domain.IterationClosed closed => new Contracts.IterationClosed(closed.ItemId, closed.IterationId),

        _ => null,
    };
}

internal sealed class PortfolioRepository(PortfolioDbContext context) : IPortfolioRepository
{
    /// <summary>
    /// Loads an item to change it, refusing early when the caller may read it but not write it.
    /// </summary>
    /// <remarks>
    /// Every caller of this repository is a command, so the check belongs here rather than in a dozen handlers.
    /// It became necessary with the catalog: before §03 a head of another branch could not see the item at all,
    /// so a write attempt died as a 404 on the load. Now they can read it — that is the whole point of a
    /// browsable catalog — and without this the aggregate would transition in memory and the UPDATE would be
    /// swallowed by RLS, surfacing as a 500. A refusal is an answer; a 500 is a bug report.
    /// </remarks>
    public async Task<PortfolioItem> GetAsync(Guid itemId, CancellationToken ct)
    {
        var item = await LoadAsync(itemId, ct);

        if (!await IsWritableAsync(itemId, ct))
        {
            throw new UnauthorizedAccessException(
                "You can see this item but not change it. Its owning branch, its lead or the PMO can.");
        }

        return item;
    }

    private async Task<bool> IsWritableAsync(Guid itemId, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();

        command.CommandText = """
            select exists (
                select 1 from portfolio.portfolio_item i
                where i.id = @id
                  and portfolio.can_write_item(
                      i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                      i.lead_person_id, i.po_person_id))
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = itemId;
        command.Parameters.Add(parameter);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task<PortfolioItem> LoadAsync(Guid itemId, CancellationToken ct) =>
        await context.Items
            .Include(item => item.Iterations)
            .Include(item => item.Epics)
            .Include(item => item.Members)
            .Include(item => item.Dependencies)
            .AsSplitQuery()
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"No portfolio item {itemId}.");

    public async Task AddAsync(PortfolioItem item, CancellationToken ct) => await context.Items.AddAsync(item, ct);

    public async Task RecordTransitionAsync(PortfolioTransition transition, CancellationToken ct) =>
        await context.Transitions.AddAsync(transition, ct);

    public async Task<bool> IsLinkedToProjectAsync(Guid projectId, CancellationToken ct) =>
        await context.Items.AnyAsync(item => item.ProjectId == projectId, ct);
}
