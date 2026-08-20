namespace Cracra.Modules.Portfolio.Domain;

/// <summary>
/// Every lifecycle move, append-only.
/// </summary>
/// <remarks>
/// <para>
/// S4's acceptance criteria call for guarded <em>and audited</em> transitions, and this is the audited half. The
/// state on the aggregate answers "where is this now"; only the trail answers "who moved it, when, and why",
/// which is the question that actually gets asked — usually about something archived a year ago.
/// </para>
/// <para>
/// Kept as its own table rather than derived from the timestamps on the item, because those are overwritten by a
/// revert. A trail that a later action can rewrite is not a trail.
/// </para>
/// </remarks>
public sealed class PortfolioTransition
{
    public required Guid Id { get; init; }

    public required Guid PortfolioItemId { get; init; }

    public required Guid DepartmentId { get; init; }

    /// <summary>Null for the initial registration, where there is no prior state.</summary>
    public PortfolioState? FromState { get; init; }

    public required PortfolioState ToState { get; init; }

    /// <summary>True when the move went backwards. PMO-only, and worth being able to query for on its own.</summary>
    public required bool IsReversal { get; init; }

    public required string Reason { get; init; }

    public required Guid DecidedBy { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }
}
