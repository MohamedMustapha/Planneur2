using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Strategy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Strategy.Data;

public sealed class StrategyDbContext(DbContextOptions<StrategyDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "strategy";

    public DbSet<StrategyPlan> Strategies => Set<StrategyPlan>();

    public DbSet<Objective> Objectives => Set<Objective>();

    public DbSet<KeyResult> KeyResults => Set<KeyResult>();

    public DbSet<ObjectiveContribution> Contributions => Set<ObjectiveContribution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StrategyDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class StrategyConfiguration : IEntityTypeConfiguration<StrategyPlan>
{
    public void Configure(EntityTypeBuilder<StrategyPlan> builder)
    {
        builder.ToTable("strategy");
        builder.HasKey(strategy => strategy.Id);

        builder.Property(strategy => strategy.Id).ValueGeneratedNever();
        builder.Property(strategy => strategy.ScopeType).HasMaxLength(16).IsRequired();
        builder.Property(strategy => strategy.Title).HasMaxLength(256).IsRequired();
        builder.Property(strategy => strategy.Narrative).HasMaxLength(16000);
        builder.Property(strategy => strategy.Status).HasMaxLength(16).IsRequired();

        // The scope id is the node id the RLS trigger derives the path from. Named scope_id on the entity because
        // that is what the spec calls it, and mapped explicitly so the migration's attach call has a column to
        // point at without the two names having to coincide by luck.
        builder.Property(strategy => strategy.ScopeId).HasColumnName("scope_id");

        builder.HasIndex(strategy => new { strategy.ScopeId, strategy.Status });

        builder.HasMany(strategy => strategy.Objectives)
            .WithOne()
            .HasForeignKey(objective => objective.StrategyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(strategy => strategy.Objectives).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ObjectiveConfiguration : IEntityTypeConfiguration<Objective>
{
    public void Configure(EntityTypeBuilder<Objective> builder)
    {
        builder.ToTable("objective");
        builder.HasKey(objective => objective.Id);

        builder.Property(objective => objective.Id).ValueGeneratedNever();
        builder.Property(objective => objective.Title).HasMaxLength(256).IsRequired();
        builder.Property(objective => objective.Description).HasMaxLength(8000);
        builder.Property(objective => objective.MetricKind).HasMaxLength(16).IsRequired();
        builder.Property(objective => objective.Unit).HasMaxLength(32);
        builder.Property(objective => objective.Status).HasMaxLength(16).IsRequired();
        builder.Property(objective => objective.Baseline).HasPrecision(18, 4);
        builder.Property(objective => objective.Target).HasPrecision(18, 4);
        builder.Property(objective => objective.Current).HasPrecision(18, 4);
        builder.Property(objective => objective.Weight).HasPrecision(8, 4);

        builder.Ignore(objective => objective.CreatedOn);

        builder.HasIndex(objective => objective.StrategyId);

        builder.HasMany(objective => objective.KeyResults)
            .WithOne()
            .HasForeignKey(keyResult => keyResult.ObjectiveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(objective => objective.Contributions)
            .WithOne()
            .HasForeignKey(contribution => contribution.ObjectiveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(objective => objective.KeyResults).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(objective => objective.Contributions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class KeyResultConfiguration : IEntityTypeConfiguration<KeyResult>
{
    public void Configure(EntityTypeBuilder<KeyResult> builder)
    {
        builder.ToTable("key_result");
        builder.HasKey(keyResult => keyResult.Id);

        builder.Property(keyResult => keyResult.Id).ValueGeneratedNever();
        builder.Property(keyResult => keyResult.Title).HasMaxLength(256).IsRequired();
        builder.Property(keyResult => keyResult.Target).HasPrecision(18, 4);
        builder.Property(keyResult => keyResult.Current).HasPrecision(18, 4);

        builder.Ignore(keyResult => keyResult.Progress);

        builder.HasIndex(keyResult => keyResult.ObjectiveId);
    }
}

internal sealed class ObjectiveContributionConfiguration : IEntityTypeConfiguration<ObjectiveContribution>
{
    public void Configure(EntityTypeBuilder<ObjectiveContribution> builder)
    {
        builder.ToTable("objective_contribution");
        builder.HasKey(contribution => contribution.Id);

        builder.Property(contribution => contribution.Id).ValueGeneratedNever();
        builder.Property(contribution => contribution.SourceType).HasMaxLength(16).IsRequired();
        builder.Property(contribution => contribution.Weight).HasPrecision(8, 4);
        builder.Property(contribution => contribution.Note).HasMaxLength(2000);

        // One link per (objective, thing). The aggregate re-weighs a repeat for a civil answer; this is what
        // holds it when the alignment view and the item card are both open.
        builder.HasIndex(contribution => new
            {
                contribution.ObjectiveId,
                contribution.SourceType,
                contribution.SourceId,
            })
            .IsUnique();

        // The reverse question, asked by every identity card: "what does this item serve".
        builder.HasIndex(contribution => new { contribution.SourceType, contribution.SourceId });
    }
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class StrategyDbContextFactory : IDesignTimeDbContextFactory<StrategyDbContext>
{
    public StrategyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Database=cracra;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<StrategyDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                "__ef_migrations_history",
                StrategyDbContext.SchemaName))
            .Options;

        return new StrategyDbContext(options);
    }
}
