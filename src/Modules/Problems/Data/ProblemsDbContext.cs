using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Problems.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Problems.Data;

public sealed class ProblemsDbContext(DbContextOptions<ProblemsDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "problems";

    public DbSet<Problem> Problems => Set<Problem>();

    public DbSet<Proposal> Proposals => Set<Proposal>();

    public DbSet<ProblemVote> Votes => Set<ProblemVote>();

    public DbSet<ProblemComment> Comments => Set<ProblemComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProblemsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ProblemConfiguration : IEntityTypeConfiguration<Problem>
{
    public void Configure(EntityTypeBuilder<Problem> builder)
    {
        builder.ToTable("problem");
        builder.HasKey(problem => problem.Id);

        builder.Property(problem => problem.Id).ValueGeneratedNever();
        builder.Property(problem => problem.Code).HasMaxLength(64).IsRequired();
        builder.Property(problem => problem.Title).HasMaxLength(256).IsRequired();
        builder.Property(problem => problem.Description).HasMaxLength(8000);
        builder.Property(problem => problem.Category).HasMaxLength(32).IsRequired();
        builder.Property(problem => problem.OriginScopeType).HasMaxLength(16).IsRequired();
        builder.Property(problem => problem.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(problem => problem.ImpactFrequency).HasConversion<string>().HasMaxLength(16);
        builder.Property(problem => problem.ImpactTimeLoss).HasPrecision(8, 2);
        builder.Property(problem => problem.DecisionReason).HasMaxLength(4000);

        builder.Ignore(problem => problem.AnnualHoursLost);
        builder.Ignore(problem => problem.VoteCount);

        builder.HasIndex(problem => problem.Code).IsUnique();
        builder.HasIndex(problem => problem.NodeId);
        builder.HasIndex(problem => problem.ReporterPersonId);

        // The board's two orderings, and the category filter the cross-node read leans on.
        builder.HasIndex(problem => new { problem.Status, problem.Category });

        builder.HasMany(problem => problem.Proposals)
            .WithOne()
            .HasForeignKey(proposal => proposal.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(problem => problem.Votes)
            .WithOne()
            .HasForeignKey(vote => vote.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(problem => problem.Comments)
            .WithOne()
            .HasForeignKey(comment => comment.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(problem => problem.Proposals).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(problem => problem.Votes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(problem => problem.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposal");
        builder.HasKey(proposal => proposal.Id);

        builder.Property(proposal => proposal.Id).ValueGeneratedNever();
        builder.Property(proposal => proposal.Description).HasMaxLength(8000).IsRequired();
        builder.Property(proposal => proposal.EffortGuess).HasPrecision(8, 2);

        builder.HasIndex(proposal => proposal.ProblemId);
    }
}

internal sealed class ProblemVoteConfiguration : IEntityTypeConfiguration<ProblemVote>
{
    public void Configure(EntityTypeBuilder<ProblemVote> builder)
    {
        builder.ToTable("problem_vote");

        // The composite key *is* the one-vote-per-person rule. The aggregate refuses a second press for a civil
        // answer; this is what holds it when two tabs press at once.
        builder.HasKey(vote => new { vote.ProblemId, vote.PersonId });

        builder.HasIndex(vote => vote.PersonId);
    }
}

internal sealed class ProblemCommentConfiguration : IEntityTypeConfiguration<ProblemComment>
{
    public void Configure(EntityTypeBuilder<ProblemComment> builder)
    {
        builder.ToTable("problem_comment");
        builder.HasKey(comment => comment.Id);

        builder.Property(comment => comment.Id).ValueGeneratedNever();
        builder.Property(comment => comment.Body).HasMaxLength(8000).IsRequired();

        builder.HasIndex(comment => comment.ProblemId);
    }
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class ProblemsDbContextFactory : IDesignTimeDbContextFactory<ProblemsDbContext>
{
    public ProblemsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Database=cracra;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ProblemsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                "__ef_migrations_history",
                ProblemsDbContext.SchemaName))
            .Options;

        return new ProblemsDbContext(options);
    }
}
