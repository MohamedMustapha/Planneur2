using Cracra.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Reporting.Infrastructure;

/// <summary>
/// A cached narrative.
/// </summary>
/// <remarks>
/// <para>
/// The module's only table, and the spec is right that it is "a small cache" rather than a domain. Reports
/// themselves are not stored: they are composed on demand from rows other modules own, which is what keeps a
/// report from becoming a second, staler copy of the truth.
/// </para>
/// <para>
/// The scope columns exist for row-level security rather than for querying. A cached summary is written from rows
/// the author was allowed to see, so it must be readable only by somebody who could have asked the same question
/// — which is what <c>access.can_read_report</c> decides.
/// </para>
/// </remarks>
public sealed class GeneratedSummary
{
    public required Guid Id { get; init; }

    public required string Scope { get; init; }

    /// <summary>The project, for a project report. Null for every scope derived from who the caller is.</summary>
    public Guid? ScopeId { get; init; }

    /// <summary>Whoever asked for it. The only reader of a "my" summary.</summary>
    public required Guid OwnerPersonId { get; init; }

    /// <summary>Denormalized so the predicate never joins into Directory, as everywhere else in the platform.</summary>
    public Guid? UnitId { get; init; }

    public Guid? DepartmentId { get; init; }

    public required string PeriodKind { get; init; }

    public required DateOnly PeriodFrom { get; init; }

    public required DateOnly PeriodTo { get; init; }

    public required string Language { get; init; }

    public required string Model { get; init; }

    /// <summary>
    /// Hash of the descriptor, the model and the figures the model was shown.
    /// </summary>
    /// <remarks>
    /// The figures are in it deliberately. Keying on the descriptor alone would serve last Monday's narrative
    /// beside this Monday's numbers, and the two would quietly disagree.
    /// </remarks>
    public required string PromptHash { get; init; }

    public required string Text { get; init; }

    /// <summary>Prompt size in characters, not tokens: the client is not a tokenizer and does not need to be.</summary>
    public required int PromptCharacters { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "reporting";

    public DbSet<GeneratedSummary> Summaries => Set<GeneratedSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class GeneratedSummaryConfiguration : IEntityTypeConfiguration<GeneratedSummary>
{
    public void Configure(EntityTypeBuilder<GeneratedSummary> builder)
    {
        builder.ToTable("generated_summary");
        builder.HasKey(summary => summary.Id);

        builder.Property(summary => summary.Scope).HasMaxLength(32).IsRequired();
        builder.Property(summary => summary.PeriodKind).HasMaxLength(32).IsRequired();
        builder.Property(summary => summary.Language).HasMaxLength(8).IsRequired();
        builder.Property(summary => summary.Model).HasMaxLength(128).IsRequired();
        builder.Property(summary => summary.PromptHash).HasMaxLength(64).IsRequired();

        // No length cap. A narrative is bounded by the model's max_tokens, and a truncating column would corrupt
        // the one thing this table exists to keep.
        builder.Property(summary => summary.Text).IsRequired();

        // The cache lookup, in one index: same question, same figures.
        builder.HasIndex(summary => new { summary.PromptHash, summary.OwnerPersonId });

        // The "latest for this report" lookup the view uses to decide staleness.
        builder.HasIndex(summary => new
        {
            summary.OwnerPersonId,
            summary.Scope,
            summary.ScopeId,
            summary.PeriodFrom,
            summary.PeriodTo,
            summary.Language,
        });
    }
}
