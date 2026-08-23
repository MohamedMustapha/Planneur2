using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Problems.Contracts;

// =================================================================================================================
// The Problems module's public surface (v2 §05).
// =================================================================================================================

public sealed record ProblemCard(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    string Category,
    string OriginScopeType,
    Guid OriginScopeId,
    Guid NodeId,
    Guid ReporterPersonId,
    string? ReporterName,
    decimal? ImpactTimeLoss,
    string ImpactFrequency,
    int? AffectedPeopleEstimate,
    decimal AnnualHoursLost,
    int VoteCount,
    int ProposalCount,
    bool VotedByMe,
    string Status,
    Guid? ConvertedItemId,
    Guid? DuplicateOfProblemId,
    string? DecisionReason,
    DateTimeOffset CreatedAt);

public sealed record ProposalView(
    Guid Id,
    Guid AuthorPersonId,
    string? AuthorName,
    string Description,
    decimal? EffortGuess,
    DateTimeOffset CreatedAt);

public sealed record ProblemCommentView(
    Guid Id,
    Guid AuthorPersonId,
    string? AuthorName,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record ProblemDetail(
    ProblemCard Card,
    IReadOnlyList<ProposalView> Proposals,
    IReadOnlyList<ProblemCommentView> Comments);

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// A problem became work somebody owns.
/// </summary>
/// <remarks>
/// Carries the item so Portfolio can show "originating problem" on the card without asking, and so a later
/// resolution can find its way back. This is the "no shadow IT" seam: the pain and the project that answers it
/// stay linked, in both directions, from the moment of conversion.
/// </remarks>
public sealed record ProblemConverted(Guid ProblemId, Guid ItemId, string Title) : IntegrationEvent;

public sealed record ProblemResolved(Guid ProblemId, Guid? ItemId) : IntegrationEvent;

// --- Problem lookup, for modules that reference a problem without drawing its card (v2 §06.1) --------------------

/// <summary>The least a problem needs to be recognised somewhere else. See Portfolio's CatalogCardRef for why.</summary>
public sealed record ProblemRef(Guid Id, string Code, string Title, string Status, Guid NodeId);

/// <summary>
/// Resolves problems by id.
/// </summary>
/// <remarks>
/// Narrower than Portfolio's equivalent — there is no "problems in scope" call, because a
/// problem nobody linked to an objective is not an alignment gap. Solving pains is what the intake is for; serving
/// a strategy is a bonus, not an expectation.
/// </remarks>
public interface IProblemLookupReader
{
    Task<IReadOnlyList<ProblemRef>> GetByIdsAsync(IReadOnlyList<Guid> problemIds, CancellationToken ct);
}
