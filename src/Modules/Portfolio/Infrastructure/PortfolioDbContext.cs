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
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.DecisionNotes).HasMaxLength(4000);

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

        builder.Navigation(item => item.Iterations).UsePropertyAccessMode(PropertyAccessMode.Field);
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
    public async Task<PortfolioItem> GetAsync(Guid itemId, CancellationToken ct) =>
        await context.Items
            .Include(item => item.Iterations)
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == itemId, ct)
            ?? throw new ResourceNotFoundException($"No portfolio item {itemId}.");

    public async Task AddAsync(PortfolioItem item, CancellationToken ct) => await context.Items.AddAsync(item, ct);

    public async Task RecordTransitionAsync(PortfolioTransition transition, CancellationToken ct) =>
        await context.Transitions.AddAsync(transition, ct);

    public async Task<bool> IsLinkedToProjectAsync(Guid projectId, CancellationToken ct) =>
        await context.Items.AnyAsync(item => item.ProjectId == projectId, ct);
}
