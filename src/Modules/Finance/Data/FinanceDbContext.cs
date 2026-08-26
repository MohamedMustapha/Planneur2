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

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<CostComponent> Components => Set<CostComponent>();

    public DbSet<License> Licenses => Set<License>();

    public DbSet<ExternalWorker> ExternalWorkers => Set<ExternalWorker>();

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

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budget");
        builder.HasKey(budget => budget.Id);

        builder.Property(budget => budget.Id).ValueGeneratedNever();
        builder.Property(budget => budget.ScopeType).HasConversion<string>().HasMaxLength(8);
        builder.Property(budget => budget.Currency).HasMaxLength(3).IsRequired();
        builder.Property(budget => budget.Notes).HasMaxLength(2000);
        builder.Property(budget => budget.PlannedAmount).HasPrecision(16, 2);

        // One envelope per thing per year. Two would make variance ambiguous, and the page has no way to show
        // which of them the head meant.
        builder.HasIndex(budget => new { budget.ScopeType, budget.ScopeId, budget.FiscalYear }).IsUnique();
        builder.HasIndex(budget => new { budget.OwnerNodeId, budget.FiscalYear });
    }
}

internal sealed class CostComponentConfiguration : IEntityTypeConfiguration<CostComponent>
{
    public void Configure(EntityTypeBuilder<CostComponent> builder)
    {
        builder.ToTable("cost_component", table => table.HasCheckConstraint(
            "ck_cost_component_owner",
            // Exactly one owner. The aggregate refuses both and neither for a readable message; this is what holds
            // it against anything that writes rows another way, and a component counted at an item *and* at its
            // node would be counted twice in the same rollup.
            "(item_id is null) <> (node_id is null)"));

        builder.HasKey(component => component.Id);

        builder.Property(component => component.Id).ValueGeneratedNever();
        builder.Property(component => component.Kind).HasConversion<string>().HasMaxLength(24);
        builder.Property(component => component.Label).HasMaxLength(256).IsRequired();
        builder.Property(component => component.Treatment).HasMaxLength(16).IsRequired();
        builder.Property(component => component.Currency).HasMaxLength(3).IsRequired();
        builder.Property(component => component.Notes).HasMaxLength(2000);
        builder.Property(component => component.Amount).HasPrecision(16, 2);

        builder.HasIndex(component => component.ItemId);
        builder.HasIndex(component => component.NodeId);
        builder.HasIndex(component => component.OwnerNodeId);
        builder.HasIndex(component => new { component.PeriodStart, component.PeriodEnd });
    }
}

internal sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("license");
        builder.HasKey(license => license.Id);

        builder.Property(license => license.Id).ValueGeneratedNever();
        builder.Property(license => license.ProductName).HasMaxLength(256).IsRequired();
        builder.Property(license => license.Vendor).HasMaxLength(256);
        builder.Property(license => license.Currency).HasMaxLength(3).IsRequired();
        builder.Property(license => license.BillingCycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(license => license.Notes).HasMaxLength(2000);
        builder.Property(license => license.UnitCost).HasPrecision(14, 2);

        builder.Ignore(license => license.AnnualCost);

        builder.HasIndex(license => license.NodeId);
        builder.HasIndex(license => license.ItemId);

        // The renewal calendar's only query: what falls due, soonest first.
        builder.HasIndex(license => license.RenewalDate);
    }
}

internal sealed class ExternalWorkerConfiguration : IEntityTypeConfiguration<ExternalWorker>
{
    public void Configure(EntityTypeBuilder<ExternalWorker> builder)
    {
        builder.ToTable("external_worker");
        builder.HasKey(worker => worker.Id);

        builder.Property(worker => worker.Id).ValueGeneratedNever();
        builder.Property(worker => worker.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(worker => worker.Vendor).HasMaxLength(256);
        builder.Property(worker => worker.Role).HasMaxLength(128);
        builder.Property(worker => worker.RateUnit).HasMaxLength(8).IsRequired();
        builder.Property(worker => worker.Currency).HasMaxLength(3).IsRequired();
        builder.Property(worker => worker.Rate).HasPrecision(12, 2);

        builder.HasIndex(worker => worker.NodeId);
        builder.HasIndex(worker => worker.ItemId);
        builder.HasIndex(worker => worker.ContractEnd);
    }
}
