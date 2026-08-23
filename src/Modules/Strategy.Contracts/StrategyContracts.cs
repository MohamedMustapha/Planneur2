using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Strategy.Contracts;

// =================================================================================================================
// The Strategy module's public surface (v2 §06).
//
// The spine that connects intent to execution: a strategy sets objectives, portfolio items and problems are linked
// as the work that moves them, and progress rolls up. Everything here is additive-only once released.
// =================================================================================================================

/// <summary>What a strategy is attached to.</summary>
/// <remarks>
/// Both values are node ids — a service is a node like any other (v2 §01.1), and the platform never names a level.
/// The distinction is kept because a service-wide strategy and a bureau's own read differently to a human, and
/// losing that would make "our strategy" and "their strategy" the same sentence. Nothing branches on it.
/// </remarks>
public static class StrategyScopeTypes
{
    public const string Service = "service";
    public const string Node = "node";

    public static readonly IReadOnlyList<string> All = [Service, Node];
}

public static class StrategyStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Draft, Active, Closed];
}

/// <summary>
/// How an objective is measured.
/// </summary>
/// <remarks>
/// The kind decides how progress is computed, and the two at the end are the honest admission that not everything
/// worth committing to has a number: a milestone is done or it is not, and a qualitative objective is whatever its
/// owner last said it was. Both still roll up — as 0 or 1 — so a strategy made of them still has a percentage.
/// </remarks>
public static class MetricKinds
{
    public const string Number = "number";
    public const string Percent = "percent";
    public const string Currency = "currency";
    public const string Milestone = "milestone";
    public const string Qualitative = "qualitative";

    public static readonly IReadOnlyList<string> All = [Number, Percent, Currency, Milestone, Qualitative];

    /// <summary>True where current/target arithmetic means something. The other two carry a status instead.</summary>
    public static bool IsMeasured(string kind) => kind is Number or Percent or Currency;
}

public static class ObjectiveStatuses
{
    public const string OnTrack = "on-track";
    public const string AtRisk = "at-risk";
    public const string OffTrack = "off-track";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [OnTrack, AtRisk, OffTrack, Done];
}

/// <summary>Where a contribution comes from — a portfolio item (§03) or a problem (§05).</summary>
public static class ContributionSources
{
    public const string Item = "item";
    public const string Problem = "problem";

    public static readonly IReadOnlyList<string> All = [Item, Problem];
}

// --- Read DTOs ---------------------------------------------------------------------------------------------------

public sealed record StrategyView(
    Guid Id,
    string ScopeType,
    Guid ScopeId,
    string? ScopeName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Title,
    string? Narrative,
    Guid OwnerPersonId,
    string? OwnerName,
    string Status,
    int ObjectiveCount,
    decimal Progress);

public sealed record KeyResultView(Guid Id, string Title, decimal Target, decimal Current, decimal Progress);

/// <param name="State">
/// The contributing thing's own state, as its module spells it — a lifecycle state for an item, a problem status
/// for a problem. Never re-derived here: an objective card showing a different word from the item's own card is
/// how people stop believing either.
/// </param>
public sealed record ContributionView(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string? Code,
    string? Name,
    string? State,
    decimal Weight,
    string? Note);

/// <param name="Progress">0..1, bounded. Milestone and qualitative objectives report 1 when done and 0 otherwise.</param>
/// <param name="ExpectedProgress">
/// Where the objective would be if it advanced evenly from its strategy's start to its own due date. Returned
/// rather than kept private because it is the whole explanation of a status: "at risk" with no sense of what was
/// expected is a colour nobody can argue with or act on.
/// </param>
public sealed record ObjectiveView(
    Guid Id,
    Guid StrategyId,
    string Title,
    string? Description,
    string MetricKind,
    decimal? Baseline,
    decimal? Target,
    decimal? Current,
    string? Unit,
    DateOnly? Due,
    string Status,
    bool StatusOverridden,
    decimal Weight,
    decimal Progress,
    decimal ExpectedProgress,
    IReadOnlyList<KeyResultView> KeyResults,
    IReadOnlyList<ContributionView> Contributions);

public sealed record StrategyRollup(StrategyView Strategy, IReadOnlyList<ObjectiveView> Objectives);

/// <param name="UnlinkedObjectives">Objectives nothing is working on — intent with no execution behind it.</param>
/// <param name="UnlinkedItems">
/// Portfolio items serving no objective. Candidates to deprioritise, deliberately not called that in the data:
/// plenty of run work legitimately serves no strategic objective, and the view is a prompt, not a verdict.
/// </param>
public sealed record AlignmentGaps(
    IReadOnlyList<ObjectiveView> UnlinkedObjectives,
    IReadOnlyList<UnlinkedItem> UnlinkedItems);

public sealed record UnlinkedItem(Guid ItemId, string Code, string Name, string Type, string State, Guid OwnerNodeId);

/// <summary>The read-only block a COPIL minute or a node report embeds (v2 §06.5, §07.3).</summary>
public sealed record StrategySummaryBlock(
    Guid StrategyId,
    string Title,
    decimal Progress,
    int ObjectiveCount,
    int OnTrackCount,
    int AtRiskCount,
    int OffTrackCount,
    int DoneCount,
    IReadOnlyList<string> Attention);

// --- Read port for other modules ---------------------------------------------------------------------------------

public sealed record ObjectiveLink(Guid ObjectiveId, Guid StrategyId, string Title, string Status, decimal Progress);

/// <summary>
/// The strategy rollup, as the rest of the system embeds it.
/// </summary>
/// <remarks>
/// Caller-scoped like every other cross-module reader: the block a CR embeds can never contain an objective its
/// reader could not have opened directly. Meetings (§07) and Reporting (S8) both consume this rather than
/// recomputing a rollup of their own, which is the only way three screens can be guaranteed to agree.
/// </remarks>
public interface IStrategyRollupReader
{
    /// <summary>The blocks in force for a node, walking up its ancestry. Empty when nothing is in force.</summary>
    Task<IReadOnlyList<StrategySummaryBlock>> GetBlocksForNodeAsync(Guid nodeId, CancellationToken ct);

    /// <summary>The objectives a given item or problem contributes to — the line an identity card shows.</summary>
    Task<IReadOnlyList<ObjectiveLink>> GetObjectivesForSourceAsync(
        string sourceType,
        Guid sourceId,
        CancellationToken ct);
}

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>Raised when work is linked to an objective, so the item's card can show what it serves.</summary>
public sealed record ContributionLinked(Guid ObjectiveId, string SourceType, Guid SourceId) : IntegrationEvent;

public sealed record ContributionUnlinked(Guid ObjectiveId, string SourceType, Guid SourceId) : IntegrationEvent;

/// <summary>Raised when an objective's measured value moves, for anything watching a target.</summary>
public sealed record ObjectiveProgressed(Guid ObjectiveId, decimal Progress, string Status) : IntegrationEvent;
