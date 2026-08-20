using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Kudos.Contracts;

// =================================================================================================================
// The Kudos module's public surface. S6 draws the monthly total on the team board and S8 counts them in the unit
// report; both read through these DTOs rather than through the schema.
// =================================================================================================================

/// <summary>
/// How much of the recognition machinery a department has switched on.
/// </summary>
/// <remarks>
/// A ladder rather than a set of independent flags: every department has a counter, some also show points, and a
/// few also rank people against each other. Modelling it as three booleans would admit combinations nobody asked
/// for — a leaderboard with no points on it — and would put the ordering rule in every caller instead of here.
/// </remarks>
public static class KudoModes
{
    /// <summary>The default. A count of recognitions, and nothing that resembles a score.</summary>
    public const string Counter = "counter";

    /// <summary>Points are shown, badges are earned; nobody is ranked against anybody.</summary>
    public const string Points = "points";

    /// <summary>Points, badges and a ranked leaderboard for the unit or department.</summary>
    public const string PointsBadgesLeaderboard = "points-badges-leaderboard";
}

/// <summary>One category a kudo can be given in, as offered by a department.</summary>
/// <param name="Points">What it is worth. Always present; whether it is <em>shown</em> is the mode's business.</param>
public sealed record KudoCategoryOption(string Code, string LabelKey, int Points);

/// <summary>
/// A badge somebody has earned.
/// </summary>
/// <remarks>
/// <see cref="EarnedAt"/> is the moment of the kudo that took them over the threshold, not the moment this was
/// computed — badges are derived from the kudos on record rather than stored as awards of their own.
/// </remarks>
public sealed record BadgeView(string Code, string LabelKey, DateTimeOffset EarnedAt);

/// <summary>One kudo, as everything on the wall renders it.</summary>
/// <param name="Points">Zero whenever the department's mode does not show points, whatever the row holds.</param>
public sealed record KudoView(
    Guid Id,
    Guid FromPersonId,
    string? FromPersonName,
    Guid ToPersonId,
    string? ToPersonName,
    Guid UnitId,
    Guid DepartmentId,
    string Category,
    string CategoryLabelKey,
    string Message,
    int Points,
    DateTimeOffset CreatedAt);

/// <summary>
/// What the give-kudo form needs to draw itself, for one prospective recipient.
/// </summary>
/// <remarks>
/// One call rather than three, because the modal cannot open usefully without all of it: the categories on offer,
/// what the giver has left this month, and whether points are worth mentioning at all.
/// </remarks>
public sealed record KudoRulesView(
    Guid DepartmentId,
    string Mode,
    bool ShowsPoints,
    bool ShowsLeaderboard,
    int MonthlyCapPerGiver,
    int GivenThisMonth,
    int RemainingThisMonth,
    IReadOnlyList<KudoCategoryOption> Categories);

/// <summary>Somebody the caller is allowed to recognise, and why they are allowed to.</summary>
/// <param name="Relation"><c>unit</c> for a unit peer, <c>project</c> for a project teammate, <c>scope</c> for a head.</param>
public sealed record EligiblePeer(Guid PersonId, string DisplayName, Guid? UnitId, string Relation);

/// <summary>Per-person totals for a scope over a period. The team board's monthly widget.</summary>
public sealed record KudoPersonTotal(
    Guid PersonId,
    string? PersonName,
    int Count,
    int Points,
    IReadOnlyList<BadgeView> Badges);

/// <summary>
/// The counter, in every mode.
/// </summary>
/// <remarks>
/// Deliberately unranked and ordered by name. A ranked list returned here would smuggle the leaderboard into
/// departments that declined it — the widget lives on a board everyone in the unit sees.
/// </remarks>
public sealed record KudosSummary(
    string Scope,
    Guid? ScopeId,
    string Mode,
    bool ShowsPoints,
    DateOnly From,
    DateOnly To,
    int Total,
    int MyReceived,
    int MyGiven,
    IReadOnlyList<KudoPersonTotal> PerPerson);

public sealed record LeaderboardRow(
    int Rank,
    Guid PersonId,
    string? PersonName,
    int Count,
    int Points,
    IReadOnlyList<BadgeView> Badges);

/// <summary>The ranked view. Only ever returned where the department's mode enables it.</summary>
public sealed record LeaderboardView(
    string Scope,
    Guid? ScopeId,
    string Mode,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<LeaderboardRow> Rows);

/// <summary>Kudos received in one category over a year, with the messages that came with them.</summary>
public sealed record AnnualCategoryGroup(
    string Category,
    string LabelKey,
    int Count,
    int Points,
    IReadOnlyList<KudoView> Kudos);

/// <summary>
/// The annual-review claim view: everything a person was recognised for in a year.
/// </summary>
/// <remarks>
/// Messages included, in full. The whole point of the screen is to be pasted into a review form, and a count
/// without the sentence that came with it proves nothing to anybody.
/// </remarks>
public sealed record AnnualKudosView(
    Guid PersonId,
    string? PersonName,
    int Year,
    string Mode,
    bool ShowsPoints,
    int Total,
    int Points,
    IReadOnlyList<AnnualCategoryGroup> ByCategory,
    IReadOnlyList<BadgeView> Badges);

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// Somebody recognised somebody else.
/// </summary>
/// <remarks>
/// Carries the unit and department so a board can decide whether it cares without loading the row: the monthly
/// widget on a unit board refreshes on this, and every other unit's board must not.
/// </remarks>
public sealed record KudoGiven(
    Guid KudoId,
    Guid FromPersonId,
    Guid ToPersonId,
    Guid UnitId,
    Guid DepartmentId,
    string Category,
    int Points) : IntegrationEvent;

/// <summary>
/// A threshold was crossed by the kudo being written.
/// </summary>
/// <remarks>
/// A fact about a moment rather than a row somewhere: badges are derived from the kudos on record, so this is what
/// makes "you have earned Cornerstone" announceable at the instant it becomes true. A consumer that missed it can
/// always ask again — the badge is still derivable tomorrow.
/// </remarks>
public sealed record BadgeAwarded(
    Guid PersonId,
    Guid DepartmentId,
    string BadgeCode,
    int PointsAtAward) : IntegrationEvent;

// --- Read port for other modules -------------------------------------------------------------------------------

/// <summary>
/// What the rest of the platform asks of Kudos.
/// </summary>
/// <remarks>
/// Implemented by Kudos, consumed through this assembly, and run inside the caller's own RLS session — so a report
/// counts exactly the kudos its reader was allowed to see, and the visibility matrix is not restated here.
/// </remarks>
public interface IKudosReader
{
    /// <summary>Kudos given to people in a unit or department over a window. Either scope may be null for "any".</summary>
    Task<int> CountAsync(Guid? unitId, Guid? departmentId, DateOnly from, DateOnly to, CancellationToken ct);
}
