using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Finance.Domain;

/// <summary>
/// A licence the organisation pays for (v2 §04.1).
/// </summary>
/// <remarks>
/// Attaches to a node or to an item, and either is legitimate: SharePoint is a product the whole service uses, an
/// IDE licence belongs to the team that bought it. It feeds the rollup from wherever it sits, which is why the
/// renewal calendar and the consolidated total can never disagree about what is being paid for.
/// </remarks>
public sealed class License
{
    private License()
    {
    }

    public Guid Id { get; private init; }

    public Guid NodeId { get; private set; }

    public Guid? ItemId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public string? Vendor { get; private set; }

    public int Seats { get; private set; }

    public decimal UnitCost { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public BillingCycle BillingCycle { get; private set; }

    public DateOnly? RenewalDate { get; private set; }

    public bool Active { get; private set; } = true;

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid ModifiedBy { get; private set; }

    /// <summary>Seats times unit cost. The number the rollup counts, not a per-seat one.</summary>
    public decimal AnnualCost => BillingCycle switch
    {
        BillingCycle.Monthly => Seats * UnitCost * 12,
        BillingCycle.Quarterly => Seats * UnitCost * 4,
        _ => Seats * UnitCost,
    };

    public static License Create(
        Guid nodeId,
        Guid? itemId,
        string productName,
        string? vendor,
        int seats,
        decimal unitCost,
        string? currency,
        BillingCycle billingCycle,
        DateOnly? renewalDate,
        string? notes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (nodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A licence needs the node that pays for it.");
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new DomainRuleViolationException("A licence needs a product name.");
        }

        if (seats < 0)
        {
            throw new DomainRuleViolationException("Seats cannot be negative.");
        }

        if (unitCost < 0)
        {
            throw new DomainRuleViolationException("A unit cost cannot be negative.");
        }

        return new License
        {
            Id = Guid.CreateVersion7(),
            NodeId = nodeId,
            ItemId = itemId,
            ProductName = productName.Trim(),
            Vendor = Blank(vendor),
            Seats = seats,
            UnitCost = unitCost,
            Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant(),
            BillingCycle = billingCycle,
            RenewalDate = renewalDate,
            Notes = Blank(notes),
            CreatedAt = now,
            ModifiedAt = now,
            ModifiedBy = modifiedBy,
        };
    }

    public void Revise(
        int? seats,
        decimal? unitCost,
        DateOnly? renewalDate,
        Guid? itemId,
        bool? active,
        string? notes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (seats is { } count)
        {
            if (count < 0)
            {
                throw new DomainRuleViolationException("Seats cannot be negative.");
            }

            Seats = count;
        }

        if (unitCost is { } cost)
        {
            if (cost < 0)
            {
                throw new DomainRuleViolationException("A unit cost cannot be negative.");
            }

            UnitCost = cost;
        }

        if (renewalDate is not null)
        {
            RenewalDate = renewalDate;
        }

        if (itemId is { } attached)
        {
            ItemId = attached == Guid.Empty ? null : attached;
        }

        if (active is { } live)
        {
            Active = live;
        }

        if (notes is not null)
        {
            Notes = Blank(notes);
        }

        ModifiedAt = now;
        ModifiedBy = modifiedBy;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A hired consultant.
/// </summary>
/// <remarks>
/// Their planned effort shows on boards like anyone else's — that is what makes a week readable — but the money
/// lives here, because an external's cost is a contract and not a rate card. Keeping the two apart is what lets
/// the consolidated view answer "how much of this is bought effort" without inferring it from hours.
/// </remarks>
public sealed class ExternalWorker
{
    private ExternalWorker()
    {
    }

    public Guid Id { get; private init; }

    public Guid NodeId { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? PersonId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Vendor { get; private set; }

    public string? Role { get; private set; }

    public decimal Rate { get; private set; }

    /// <summary>day or hour — what <see cref="Rate"/> is per.</summary>
    public string RateUnit { get; private set; } = RateUnits.Day;

    public string Currency { get; private set; } = "EUR";

    public DateOnly ContractStart { get; private set; }

    public DateOnly? ContractEnd { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid ModifiedBy { get; private set; }

    public static ExternalWorker Create(
        Guid nodeId,
        Guid? itemId,
        Guid? personId,
        string displayName,
        string? vendor,
        string? role,
        decimal rate,
        string? rateUnit,
        string? currency,
        DateOnly contractStart,
        DateOnly? contractEnd,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (nodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("An external worker needs the node that engaged them.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainRuleViolationException("An external worker needs a name.");
        }

        if (rate < 0)
        {
            throw new DomainRuleViolationException("A rate cannot be negative.");
        }

        var unit = string.IsNullOrWhiteSpace(rateUnit) ? RateUnits.Day : rateUnit.Trim().ToLowerInvariant();

        if (!RateUnits.All.Contains(unit, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{rateUnit}' is not a rate unit.");
        }

        if (contractEnd is { } end && end < contractStart)
        {
            throw new DomainRuleViolationException("A contract cannot end before it starts.");
        }

        return new ExternalWorker
        {
            Id = Guid.CreateVersion7(),
            NodeId = nodeId,
            ItemId = itemId,
            PersonId = personId,
            DisplayName = displayName.Trim(),
            Vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            Rate = rate,
            RateUnit = unit,
            Currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant(),
            ContractStart = contractStart,
            ContractEnd = contractEnd,
            CreatedAt = now,
            ModifiedAt = now,
            ModifiedBy = modifiedBy,
        };
    }

    public void Revise(
        decimal? rate,
        DateOnly? contractEnd,
        Guid? itemId,
        bool? active,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (rate is { } revised)
        {
            if (revised < 0)
            {
                throw new DomainRuleViolationException("A rate cannot be negative.");
            }

            Rate = revised;
        }

        if (contractEnd is { } end)
        {
            if (end < ContractStart)
            {
                throw new DomainRuleViolationException("A contract cannot end before it starts.");
            }

            ContractEnd = end;
        }

        if (itemId is { } attached)
        {
            ItemId = attached == Guid.Empty ? null : attached;
        }

        if (active is { } live)
        {
            Active = live;
        }

        ModifiedAt = now;
        ModifiedBy = modifiedBy;
    }
}

public static class RateUnits
{
    public const string Day = "day";
    public const string Hour = "hour";

    public static readonly IReadOnlyList<string> All = [Day, Hour];
}
