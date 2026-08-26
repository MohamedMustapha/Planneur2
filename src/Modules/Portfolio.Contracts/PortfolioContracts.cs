using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Portfolio.Contracts;

// =================================================================================================================
// The Portfolio module's public surface. Scheduling (S6) opens and freezes boards off the lifecycle events, and
// Reporting (S8) flags phase status from them.
// =================================================================================================================

public sealed record IterationSummary(
    Guid Id,
    int Sequence,
    string Name,
    string Length,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string State);

public sealed record PortfolioItemSummary(
    Guid Id,
    Guid? ProjectId,
    string Name,
    string State,
    int Priority,
    Guid DepartmentId,
    string DepartmentNameKey,
    string? DecisionNotes,
    decimal? CostAmount,
    string? CostCurrency,
    string? Classification,
    IterationSummary? CurrentIteration,
    int IterationCount);

/// <summary>The board, already grouped by lane. The client renders these in the order given.</summary>
public sealed record PortfolioBoard(IReadOnlyList<PortfolioLane> Lanes);

public sealed record PortfolioLane(string State, IReadOnlyList<PortfolioItemSummary> Items);

public sealed record PortfolioItemDetail(
    PortfolioItemSummary Item,
    IReadOnlyList<IterationSummary> Iterations,
    IReadOnlyList<TransitionRecord> History);

public sealed record TransitionRecord(
    string? FromState,
    string ToState,
    bool IsReversal,
    string Reason,
    Guid DecidedBy,
    DateTimeOffset DecidedAt);

// --- Integration events ----------------------------------------------------------------------------------------

public sealed record ItemCommitted(Guid ItemId, Guid ProjectId) : IntegrationEvent;

/// <summary>Scheduling opens the item's boards on this.</summary>
public sealed record ItemActivated(Guid ItemId, Guid? ProjectId) : IntegrationEvent;

/// <summary>
/// Scheduling marks the item's rows read-only on this, and Reporting stops counting it as running.
/// </summary>
/// <remarks>
/// Carries the project id because consumers key on the project, not the portfolio item — they never needed to know
/// the portfolio existed until the moment it froze their board.
/// </remarks>
public sealed record ItemArchived(Guid ItemId, Guid? ProjectId, string Reason) : IntegrationEvent;

public sealed record IterationOpened(Guid ItemId, Guid IterationId, DateOnly StartsOn, DateOnly EndsOn)
    : IntegrationEvent;

public sealed record IterationClosed(Guid ItemId, Guid IterationId) : IntegrationEvent;

/// <summary>
/// Iteration ranges for a project, for modules that draw them.
/// </summary>
/// <remarks>
/// Keyed by project rather than by portfolio item, because that is the id every consumer already has: S6 renders
/// a project board and should not have to discover whether the project has a portfolio item at all.
/// </remarks>
public interface IPortfolioIterationReader
{
    Task<IReadOnlyList<IterationSummary>> GetForProjectAsync(Guid projectId, CancellationToken ct);
}

/// <summary>
/// The lifecycle board, for modules that report on it.
/// </summary>
/// <remarks>
/// Added for S8, whose department and portfolio reports both need "counts by state". The board is already
/// computed exactly this way for the screen, and a report re-deriving it from the items would be a second
/// implementation of the lane grouping — which is precisely how a report and a board start disagreeing.
///
/// Caller-scoped: the lanes come back narrowed to what this reader may see, so the counts in a report can never
/// exceed the counts on their own board.
/// </remarks>
public interface IPortfolioBoardReader
{
    Task<PortfolioBoard> GetBoardAsync(Guid? departmentId, CancellationToken ct);
}

// =================================================================================================================
// The catalog (v2 §03.2).
//
// An identity card is what the organisation looks at to decide whether a thing already exists. It carries enough
// to recognise the thing and to know who to ask, and stops short of anything that needs a second query per card —
// the drawer is where the full team, the epics and the dependency graph live.
// =================================================================================================================

/// <summary>Every lifecycle state, as the wire spells them.</summary>
public static class LifecycleStates
{
    public const string Considered = "considered";
    public const string Committed = "committed";
    public const string Active = "active";
    public const string AwaitingVnext = "awaiting-vnext";
    public const string Dephase = "dephase";

    public static readonly IReadOnlyList<string> All =
        [Considered, Committed, Active, AwaitingVnext, Dephase];
}

public sealed record CatalogCard(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string? Category,
    string Classification,
    string State,
    string? AwaitingVersion,
    Guid OwnerNodeId,
    Guid? LeadPersonId,
    Guid? PoPersonId,
    string? Summary,
    decimal? EstimateAmount,
    string Currency,
    int TeamHeadcount,
    int DependencyCount,
    int ConsumedByCount,
    int QueuedEpicCount,
    string? CurrentIterationName,
    bool Confidential);

/// <summary>One person on an item, grouped by where they contribute from.</summary>
public sealed record ItemTeamMember(
    Guid PersonId,
    string? DisplayName,
    Guid NodeId,
    string? NodeName,
    Guid? FunctionalRoleId,
    int? AllocationPercent,
    DateOnly From,
    DateOnly? To);

public sealed record ItemEpicView(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    string? TargetVersion,
    Guid? IterationId,
    int Sequence);

/// <param name="Direction">consumes when this item leans on the other, consumed-by when the other leans on it.</param>
public sealed record ItemDependencyView(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string ItemType,
    string Kind,
    string Direction,
    string? Note);

public sealed record CatalogItemDetail(
    CatalogCard Card,
    IReadOnlyList<ItemTeamMember> Team,
    IReadOnlyList<IterationSummary> Iterations,
    IReadOnlyList<ItemEpicView> Epics,
    IReadOnlyList<ItemDependencyView> Dependencies,
    IReadOnlyList<TransitionRecord> History);

/// <summary>
/// Creates a portfolio item on behalf of another module.
/// </summary>
/// <remarks>
/// Implemented by Portfolio, called by Problems when a pain is converted (v2 §05.2). Deliberately narrow: the
/// caller says what the thing is and who owns it, and every rule about whether they may — including which node
/// they can own it at — stays inside Portfolio and its RLS, exactly as if the wizard had been used.
/// </remarks>
public interface IPortfolioItemProvisioner
{
    Task<Guid> CreateAsync(
        string name,
        string? summary,
        string? type,
        string? category,
        Guid ownerNodeId,
        CancellationToken ct);
}

// =================================================================================================================
// Item lookup, for modules that reference an item without drawing its card (v2 §06.2).
// =================================================================================================================

/// <summary>
/// The least an item needs to be recognised somewhere else.
/// </summary>
/// <remarks>
/// Deliberately smaller than <see cref="CatalogCard"/>. An objective card shows what work is behind it and that
/// work's own state; giving it the full card would have Strategy re-rendering the portfolio's screen, and the two
/// would drift the first time either changed.
/// </remarks>
public sealed record CatalogCardRef(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string State,
    Guid OwnerNodeId,
    /// <summary>The delivery row behind it, where one exists. Null for an item nobody has started running.</summary>
    Guid? ProjectId = null);

/// <summary>
/// Resolves items by id, and lists them under a node.
/// </summary>
/// <remarks>
/// Caller-scoped like every other cross-module reader, which is what makes the alignment view honest: an item the
/// reader may not see is neither a contribution they can read nor a gap they are told to fill.
/// </remarks>
public interface ICatalogLookupReader
{
    Task<IReadOnlyList<CatalogCardRef>> GetByIdsAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct);

    /// <summary>Every item the caller may see owned inside a node's subtree.</summary>
    Task<IReadOnlyList<CatalogCardRef>> GetInScopeAsync(Guid nodeId, CancellationToken ct);
}

/// <summary>An open iteration and the item it belongs to, closing soon.</summary>
public sealed record IterationDeadline(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid IterationId,
    string IterationName,
    DateOnly EndsOn);

/// <summary>Iterations about to close, for the shell's guidance (v2 02.5).</summary>
public interface IIterationDeadlineReader
{
    Task<IReadOnlyList<IterationDeadline>> GetClosingAsync(DateOnly by, CancellationToken ct);
}
