using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Finance.Domain;

/// <summary>
/// What an envelope is drawn around (v2 §04.1).
/// </summary>
/// <remarks>
/// Any node at any level may carry one, which is the whole point: "budget par direction", "par département" and
/// "par équipe" stop being three features and become one, because a parent's envelope is compared against the sum
/// of its subtree whatever that subtree happens to contain.
/// </remarks>
public enum BudgetScope
{
    Node = 0,
    Item = 1,
}

public static class BudgetScopes
{
    public const string Node = "node";
    public const string Item = "item";

    public static readonly IReadOnlyList<string> All = [Node, Item];

    public static BudgetScope Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Node => BudgetScope.Node,
        Item => BudgetScope.Item,
        _ => throw new DomainRuleViolationException($"'{code}' is not a budget scope."),
    };
}

public enum CostKind
{
    InternalEffort = 0,
    License = 1,
    ExternalWorker = 2,
    Cloud = 3,
    Hardware = 4,
    ServiceFee = 5,
    Other = 6,
}

public static class CostKinds
{
    public const string InternalEffort = "internal-effort";
    public const string License = "license";
    public const string ExternalWorker = "external-worker";
    public const string Cloud = "cloud";
    public const string Hardware = "hardware";
    public const string ServiceFee = "service-fee";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [InternalEffort, License, ExternalWorker, Cloud, Hardware, ServiceFee, Other];

    public static CostKind Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        InternalEffort => CostKind.InternalEffort,
        License => CostKind.License,
        ExternalWorker => CostKind.ExternalWorker,
        Cloud => CostKind.Cloud,
        Hardware => CostKind.Hardware,
        ServiceFee => CostKind.ServiceFee,
        Other => CostKind.Other,
        _ => throw new DomainRuleViolationException($"'{code}' is not a cost kind."),
    };

    public static string Wire(CostKind kind) => kind switch
    {
        CostKind.InternalEffort => InternalEffort,
        CostKind.ExternalWorker => ExternalWorker,
        CostKind.ServiceFee => ServiceFee,
        _ => kind.ToString().ToLowerInvariant(),
    };
}

public enum BillingCycle
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
    OneOff = 3,
}

public static class BillingCycles
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";
    public const string OneOff = "one-off";

    public static readonly IReadOnlyList<string> All = [Monthly, Quarterly, Yearly, OneOff];

    public static BillingCycle Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Monthly => BillingCycle.Monthly,
        Quarterly => BillingCycle.Quarterly,
        Yearly => BillingCycle.Yearly,
        OneOff => BillingCycle.OneOff,
        _ => throw new DomainRuleViolationException($"'{code}' is not a billing cycle."),
    };

    public static string Wire(BillingCycle cycle) => cycle switch
    {
        BillingCycle.OneOff => OneOff,
        _ => cycle.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// A planned envelope for a node or an item, for one fiscal year.
/// </summary>
/// <remarks>
/// Deliberately separate from what things cost. A budget is a decision somebody took in advance; a cost is what
/// happened. Storing them in one place would make variance impossible to state honestly, which is the only number
/// on the page anybody argues about.
/// </remarks>
public sealed class Budget
{
    private Budget()
    {
    }

    public Guid Id { get; private init; }

    public BudgetScope ScopeType { get; private init; }

    public Guid ScopeId { get; private init; }

    /// <summary>
    /// The node this envelope rolls up under. Equal to <see cref="ScopeId"/> for a node budget, and the item's
    /// owning node for an item budget.
    /// </summary>
    /// <remarks>
    /// Always set, so the consolidation is one indexed scan over the node tree rather than a join into another
    /// module's schema — which is also what keeps the finance policies from reaching into portfolio.
    /// </remarks>
    public Guid OwnerNodeId { get; private init; }

    public int FiscalYear { get; private init; }

    public decimal PlannedAmount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid ModifiedBy { get; private set; }

    public static Budget For(
        BudgetScope scopeType,
        Guid scopeId,
        Guid ownerNodeId,
        int fiscalYear,
        decimal plannedAmount,
        string? currency,
        string? notes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (scopeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A budget needs something to be the budget of.");
        }

        if (plannedAmount < 0)
        {
            throw new DomainRuleViolationException("A planned amount cannot be negative.");
        }

        if (fiscalYear is < 2000 or > 2100)
        {
            throw new DomainRuleViolationException($"{fiscalYear} is not a plausible fiscal year.");
        }

        if (ownerNodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A budget needs the node it rolls up under.");
        }

        return new Budget
        {
            Id = Guid.CreateVersion7(),
            ScopeType = scopeType,
            ScopeId = scopeId,
            OwnerNodeId = ownerNodeId,
            FiscalYear = fiscalYear,
            PlannedAmount = plannedAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = now,
            ModifiedAt = now,
            ModifiedBy = modifiedBy,
        };
    }

    public void Revise(decimal plannedAmount, string? notes, Guid modifiedBy, DateTimeOffset now)
    {
        if (plannedAmount < 0)
        {
            throw new DomainRuleViolationException("A planned amount cannot be negative.");
        }

        PlannedAmount = plannedAmount;
        Notes = string.IsNullOrWhiteSpace(notes) ? Notes : notes.Trim();
        ModifiedAt = now;
        ModifiedBy = modifiedBy;
    }
}

/// <summary>
/// One line of what something costs.
/// </summary>
/// <remarks>
/// <para>
/// Attached to an item or to a node, and the choice matters: a licence bought for one product belongs to the
/// product, a site-wide licence belongs to the node that pays for it, and forcing the second to name an item
/// would mean inventing a fake one. Exactly one of the two is set.
/// </para>
/// <para>
/// The capex/opex split is defaulted from the node's rule and then overridable here, because the rule is right
/// most of the time and wrong often enough that an unoverridable default would be quietly falsified in a
/// spreadsheet instead.
/// </para>
/// </remarks>
public sealed class CostComponent
{
    private CostComponent()
    {
    }

    public Guid Id { get; private init; }

    public Guid? ItemId { get; private init; }

    public Guid? NodeId { get; private init; }

    /// <summary>The node this cost rolls up under, whichever end it is attached to. Always set.</summary>
    public Guid OwnerNodeId { get; private init; }

    public CostKind Kind { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string Treatment { get; private set; } = Treatments.Opex;

    /// <summary>True when somebody set the treatment by hand rather than taking the node's rule.</summary>
    public bool TreatmentOverridden { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public Guid? LicenseId { get; private set; }

    public Guid? ExternalWorkerId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid ModifiedBy { get; private set; }

    public bool CoversYear(int fiscalYear) =>
        PeriodStart.Year <= fiscalYear && PeriodEnd.Year >= fiscalYear;

    public static CostComponent Create(
        Guid? itemId,
        Guid? nodeId,
        Guid ownerNodeId,
        CostKind kind,
        string label,
        string treatment,
        bool treatmentOverridden,
        decimal amount,
        string? currency,
        DateOnly periodStart,
        DateOnly periodEnd,
        Guid? licenseId,
        Guid? externalWorkerId,
        string? notes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (itemId is null && nodeId is null)
        {
            throw new DomainRuleViolationException("A cost belongs to an item or to a node.");
        }

        if (itemId is not null && nodeId is not null)
        {
            throw new DomainRuleViolationException(
                "A cost belongs to an item or to a node, not to both: it would be counted twice in the rollup.");
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DomainRuleViolationException("A cost needs a label somebody will recognise on a report.");
        }

        if (amount < 0)
        {
            throw new DomainRuleViolationException("A cost cannot be negative.");
        }

        if (periodEnd < periodStart)
        {
            throw new DomainRuleViolationException("A cost period cannot end before it starts.");
        }

        if (!Treatments.All.Contains(treatment, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{treatment}' is not a capex/opex treatment.");
        }

        if (ownerNodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A cost needs the node it rolls up under.");
        }

        return new CostComponent
        {
            Id = Guid.CreateVersion7(),
            ItemId = itemId,
            NodeId = nodeId,
            OwnerNodeId = ownerNodeId,
            Kind = kind,
            Label = label.Trim(),
            Treatment = treatment,
            TreatmentOverridden = treatmentOverridden,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LicenseId = licenseId,
            ExternalWorkerId = externalWorkerId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = now,
            ModifiedAt = now,
            ModifiedBy = modifiedBy,
        };
    }

    public void Revise(
        string? label,
        decimal? amount,
        string? treatment,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        string? notes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (label is not null)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new DomainRuleViolationException("A cost needs a label.");
            }

            Label = label.Trim();
        }

        if (amount is { } revised)
        {
            if (revised < 0)
            {
                throw new DomainRuleViolationException("A cost cannot be negative.");
            }

            Amount = revised;
        }

        if (treatment is not null)
        {
            if (!Treatments.All.Contains(treatment, StringComparer.Ordinal))
            {
                throw new DomainRuleViolationException($"'{treatment}' is not a capex/opex treatment.");
            }

            Treatment = treatment;
            TreatmentOverridden = true;
        }

        var start = periodStart ?? PeriodStart;
        var end = periodEnd ?? PeriodEnd;

        if (end < start)
        {
            throw new DomainRuleViolationException("A cost period cannot end before it starts.");
        }

        PeriodStart = start;
        PeriodEnd = end;

        if (notes is not null)
        {
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        }

        ModifiedAt = now;
        ModifiedBy = modifiedBy;
    }
}
