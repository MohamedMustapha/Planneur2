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
