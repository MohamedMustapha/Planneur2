using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Finance.Data;

/// <summary>
/// The two configuration tables, and nothing else.
/// </summary>
/// <remarks>
/// The spec sketches derived views — SQL or materialized — for effort, cost and the split. They are not here, and
/// the reason is the isolation rule rather than performance: every input lives in another module's schema, so a
/// view would have <c>finance</c> selecting from <c>projects.project</c> and <c>activities.activity_entry</c>.
/// That is precisely the coupling architecture.md §3 forbids, expressed in the one language where no architecture
/// test can see it — and it would bypass the caller's RLS session, which is the whole security model of a
/// head-scoped view. The derivation lives in <c>CapexOpexService</c>, over each module's own contract.
/// </remarks>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "finance";

    public DbSet<RateCard> RateCards => Set<RateCard>();

    public DbSet<CapexOpexRule> Rules => Set<CapexOpexRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("rate_card", table => table.HasCheckConstraint(
            "ck_rate_card_range",
            // An open-ended card is the normal case; a card that ends before it starts is a typo the database
            // should refuse rather than a state every reader has to defend against.
            "effective_to is null or effective_to > effective_from"));

        builder.HasKey(card => card.Id);

        builder.Property(card => card.Currency).HasMaxLength(3).IsRequired();

        // Two decimals, and enough digits to price a consultant in a currency with a small unit. Precision is
        // declared rather than defaulted because Npgsql's default numeric is unconstrained, and an hourly rate
        // that silently keeps eight decimals makes every derived total disagree with the one on screen.
        builder.Property(card => card.HourlyRate).HasPrecision(12, 2);

        // The read the valuation runs once per report: this department's cards, whatever the role.
        builder.HasIndex(card => new { card.DepartmentId, card.FunctionalRoleId, card.EffectiveFrom });
    }
}

internal sealed class CapexOpexRuleConfiguration : IEntityTypeConfiguration<CapexOpexRule>
{
    public void Configure(EntityTypeBuilder<CapexOpexRule> builder)
    {
        builder.ToTable("capex_opex_rule", table => table.HasCheckConstraint(
            "ck_capex_opex_rule_treatments",
            // The same closed set the service validates, held by the database as well. Both exist on purpose: the
            // service explains, the constraint holds — including against a future migration that backfills rows.
            """
            build_treatment in ('capex', 'opex', 'excluded')
            and run_treatment in ('capex', 'opex', 'excluded')
            and qol_treatment in ('capex', 'opex', 'excluded')
            and admin_treatment in ('capex', 'opex', 'excluded')
            """));

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.BuildTreatment).HasMaxLength(16).IsRequired();
        builder.Property(rule => rule.RunTreatment).HasMaxLength(16).IsRequired();
        builder.Property(rule => rule.QolTreatment).HasMaxLength(16).IsRequired();
        builder.Property(rule => rule.AdminTreatment).HasMaxLength(16).IsRequired();

        // One rule per department, enforced rather than assumed: the service reads it with a single-or-default,
        // and a second row would make which treatment applies depend on row order.
        builder.HasIndex(rule => rule.DepartmentId).IsUnique();
    }
}
