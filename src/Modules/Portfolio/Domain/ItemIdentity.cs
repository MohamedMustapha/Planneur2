using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Portfolio.Domain;

/// <summary>
/// What kind of thing this is (v2 §03.1).
/// </summary>
/// <remarks>
/// The portfolio stopped being a list of build endeavours. A shared cluster, a bought product operated as a
/// service, a recurring public service and a bilateral summit are all things the organisation runs, all things
/// somebody should find before asking for a new one, and none of them is a project. Modelling them as one entity
/// with a type is what makes the catalog a complete map rather than a list of the work that happened to start as
/// a project.
/// </remarks>
public enum ItemType
{
    Project = 0,
    Platform = 1,
    Product = 2,
    RunService = 3,
    BusinessInitiative = 4,
    Intelligence = 5,
}

/// <summary>
/// Build, run, or both — orthogonal to <see cref="ItemType"/>.
/// </summary>
/// <remarks>
/// Deliberately a separate axis. A product is usually run and a project usually build, but a platform being
/// actively extended is both, and folding the two together would force a lie in whichever case is rarer.
/// </remarks>
public enum ItemClassification
{
    Build = 0,
    Run = 1,
    Mixed = 2,
}

public static class ItemTypes
{
    public const string Project = "project";
    public const string Platform = "platform";
    public const string Product = "product";
    public const string RunService = "run-service";
    public const string BusinessInitiative = "business-initiative";
    public const string Intelligence = "intelligence";

    public static readonly IReadOnlyList<string> All =
        [Project, Platform, Product, RunService, BusinessInitiative, Intelligence];

    public static ItemType Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Project => ItemType.Project,
        Platform => ItemType.Platform,
        Product => ItemType.Product,
        RunService => ItemType.RunService,
        BusinessInitiative => ItemType.BusinessInitiative,
        Intelligence => ItemType.Intelligence,
        _ => throw new DomainRuleViolationException($"'{code}' is not a portfolio item type."),
    };
}

public static class ItemClassifications
{
    public const string Build = "build";
    public const string Run = "run";
    public const string Mixed = "mixed";

    public static readonly IReadOnlyList<string> All = [Build, Run, Mixed];

    public static ItemClassification Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Build => ItemClassification.Build,
        Run => ItemClassification.Run,
        Mixed => ItemClassification.Mixed,
        _ => throw new DomainRuleViolationException($"'{code}' is not a classification."),
    };
}

/// <summary>How one item leans on another (v2 §03.1).</summary>
public enum DependencyKind
{
    Consumes = 0,
    Integrates = 1,
    Blocks = 2,
}

public static class DependencyKinds
{
    public const string Consumes = "consumes";
    public const string Integrates = "integrates";
    public const string Blocks = "blocks";

    public static readonly IReadOnlyList<string> All = [Consumes, Integrates, Blocks];

    public static DependencyKind Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Consumes => DependencyKind.Consumes,
        Integrates => DependencyKind.Integrates,
        Blocks => DependencyKind.Blocks,
        _ => throw new DomainRuleViolationException($"'{code}' is not a dependency kind."),
    };
}

/// <summary>
/// One item's reliance on another.
/// </summary>
/// <remarks>
/// The edge that answers both catalog questions: "what does this consume" on the item, and "who consumes this" on
/// the platform. Stored once and read from both ends, so the two can never disagree.
/// </remarks>
public sealed class ItemDependency
{
    private ItemDependency()
    {
    }

    public Guid Id { get; private init; }

    public Guid ItemId { get; private init; }

    public Guid DependsOnItemId { get; private init; }

    public DependencyKind Kind { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public static ItemDependency Between(
        Guid itemId,
        Guid dependsOnItemId,
        DependencyKind kind,
        string? note,
        DateTimeOffset now)
    {
        if (itemId == dependsOnItemId)
        {
            throw new DomainRuleViolationException("An item cannot depend on itself.");
        }

        return new ItemDependency
        {
            Id = Guid.CreateVersion7(),
            ItemId = itemId,
            DependsOnItemId = dependsOnItemId,
            Kind = kind,
            Note = note?.Trim(),
            CreatedAt = now,
        };
    }
}

/// <summary>
/// Somebody on an item, tagged with where they sit.
/// </summary>
/// <remarks>
/// The node is carried on the membership rather than looked up from the person, because the grouped view the
/// catalog draws — node, then unit, then function — has to survive somebody moving branch. What it records is
/// who was contributing from where, which is a fact about the item and not about the person's current desk.
/// </remarks>
public sealed class ItemMember
{
    private ItemMember()
    {
    }

    public Guid Id { get; private init; }

    public Guid ItemId { get; private init; }

    public Guid PersonId { get; private init; }

    public Guid NodeId { get; private set; }

    public Guid? FunctionalRoleId { get; private set; }

    public int? AllocationPercent { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly? To { get; private set; }

    public bool IsCurrentOn(DateOnly asOf) => From <= asOf && (To is null || To >= asOf);

    public static ItemMember Join(
        Guid itemId,
        Guid personId,
        Guid nodeId,
        Guid? functionalRoleId,
        int? allocationPercent,
        DateOnly from,
        DateOnly? to)
    {
        if (nodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A team member needs the node they contribute from.");
        }

        if (allocationPercent is < 0 or > 100)
        {
            throw new DomainRuleViolationException("Allocation is a percentage between 0 and 100.");
        }

        if (to is { } end && end < from)
        {
            throw new DomainRuleViolationException("A membership cannot end before it starts.");
        }

        return new ItemMember
        {
            Id = Guid.CreateVersion7(),
            ItemId = itemId,
            PersonId = personId,
            NodeId = nodeId,
            FunctionalRoleId = functionalRoleId,
            AllocationPercent = allocationPercent,
            From = from,
            To = to,
        };
    }

    public void Leave(DateOnly on) => To = on;
}
