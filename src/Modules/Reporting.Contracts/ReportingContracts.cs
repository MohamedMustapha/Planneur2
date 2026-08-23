using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Reporting.Contracts;

// =================================================================================================================
// The Reporting module's public surface.
//
// Reporting is the one module that consumes almost every other and is consumed by almost none — so this assembly
// is deliberately thin. What it does carry is the report's shape, because the client renders it directly and the
// PDF exporter renders the same thing again.
// =================================================================================================================

/// <summary>
/// The six scopes from <c>visibility-matrix.md §6</c>.
/// </summary>
/// <remarks>
/// Codes, not an enum, for the same reason every other vocabulary in this system is: they appear in URLs, in
/// cache keys and in translation keys, and a renamed enum member would silently invalidate all three.
/// </remarks>
public static class ReportScopes
{
    /// <summary>The viewer's own week. The one scope everybody has.</summary>
    public const string My = "my";

    /// <summary>The teams of the projects the viewer is on.</summary>
    public const string Team = "team";

    public const string Unit = "unit";
    public const string Department = "department";

    /// <summary>One project, for whoever leads it.</summary>
    public const string Project = "project";

    /// <summary>Everything, for the PMO.</summary>
    public const string Portfolio = "portfolio";

    public static readonly IReadOnlyList<string> All = [My, Team, Unit, Department, Project, Portfolio];
}

public static class ReportPeriods
{
    public const string Week = "week";
    public const string Month = "month";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Week, Month, Custom];
}

/// <summary>The window a report covers, resolved to real dates.</summary>
/// <param name="LabelKey">Keyed, with the numbers as parameters — the server has no idea what language this is.</param>
public sealed record ReportPeriodView(
    string Kind,
    DateOnly From,
    DateOnly To,
    int? IsoYear,
    int? IsoWeek,
    string LabelKey);

/// <summary>
/// One headline number.
/// </summary>
/// <param name="Unit">hours / count / percent / currency — decides the formatting, which is the client's job.</param>
public sealed record ReportMetric(string Key, decimal Value, string Unit);

/// <summary>
/// One row of a table.
/// </summary>
/// <param name="Label">
/// A translation key or a literal. The client renders whichever resolves — the same fallback the board timeline
/// and the meeting manager already use, because a row can be an activity bucket (vocabulary, keyed) or a person
/// (data, literal) and the report has both in the same table.
/// </param>
public sealed record ReportRow(string Key, string Label, IReadOnlyList<decimal> Values);

/// <summary>
/// A table of the report.
/// </summary>
/// <param name="IdentifiesPeople">
/// True where a row's label is somebody's name rather than vocabulary. It is what the AI prompt builder masks on:
/// people are pseudonymized before anything leaves for the model, while project, unit and department names are
/// not — those are exactly what a narrative about déphasé projects has to be able to say out loud.
/// </param>
public sealed record ReportTable(
    string TitleKey,
    IReadOnlyList<string> ColumnKeys,
    IReadOnlyList<ReportRow> Rows,
    bool IdentifiesPeople = false);

/// <summary>
/// A short remark the report makes about itself.
/// </summary>
/// <remarks>
/// Not narrative — that is the LLM's job and it is clearly labelled as such. A note is a fact the deterministic
/// side established and wants to say in words: an audit next week, a project that went déphasé, a week over
/// target. <paramref name="Text"/> carries data (a project name, a date); <paramref name="Key"/> carries the
/// sentence around it.
/// </remarks>
public sealed record ReportNote(string Key, string? Text, string? Severity);

public sealed record ReportSection(
    string Key,
    string TitleKey,
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportTable> Tables,
    IReadOnlyList<ReportNote> Notes);

/// <summary>The AI narrative, and everything needed to decide whether to trust it.</summary>
/// <param name="Stale">True when the numbers moved since the text was written. The client offers a regenerate.</param>
public sealed record ReportSummaryView(
    Guid Id,
    string Text,
    string Model,
    string Language,
    DateTimeOffset CreatedAt,
    bool Stale);

/// <summary>
/// A whole report.
/// </summary>
/// <param name="Id">
/// An opaque, deterministic description of the request rather than a stored row — see the report id helper in the
/// module. The same scope, period and language always produce the same id, and exporting one re-composes it under
/// the caller's own session rather than reading back somebody else's snapshot.
/// </param>
/// <param name="AvailableScopes">
/// Which scopes this viewer may ask for. The client draws its narrower from this rather than from role names, so
/// the widening rule lives on the server only.
/// </param>
public sealed record ReportView(
    string Id,
    string Scope,
    Guid? ScopeId,
    string ScopeLabel,
    ReportPeriodView Period,
    string Language,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<string> AvailableScopes,
    ReportSummaryView? Summary,
    DateTimeOffset GeneratedAt,
    /// <summary>
    /// The node profile's headline sentence, rendered from this report's own figures (v2 §10.5).
    /// </summary>
    /// <remarks>
    /// Null where no profile is in force, where the profile configures no pattern, or where the scope spans
    /// branches. All three are the same answer from the reader's point of view — the report opens with its own
    /// heading — so they are one nullable field rather than a state to distinguish.
    /// </remarks>
    string? Headline = null);

/// <summary>A stored export and the short-lived link to fetch it.</summary>
public sealed record ReportExportView(string ReportId, string Format, Uri Url, DateTimeOffset ExpiresAt, long Bytes);

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// Raised when an AI narrative is written.
/// </summary>
/// <remarks>
/// S8's own spec says it "emits nothing critical", and this is the shape of that: nobody must act on it. It exists
/// so the dev box, the metrics and any future audit of what the model was asked have one place to observe.
/// </remarks>
public sealed record SummaryGenerated(
    Guid SummaryId,
    string Scope,
    Guid? ScopeId,
    string Language,
    string Model,
    int PromptCharacters,
    int CompletionCharacters) : IntegrationEvent;

// =================================================================================================================
// The node brief (v2 §01.4).
//
// One shape at every depth. A brief for a node returns what its directly-attached people did, plus one block per
// direct child already aggregated over that child's whole subtree — so a head at any level can hand their brief
// upward and it slots into their parent's report as a single block, instead of being copy-pasted into it.
// =================================================================================================================

/// <summary>Deterministic totals. Every number here is computed in code; the model only ever writes prose.</summary>
public sealed record BriefTotals(
    decimal ActualHours,
    decimal PlannedHours,
    int EntryCount,
    int PeopleCount)
{
    public static readonly BriefTotals Zero = new(0m, 0m, 0, 0);

    public static BriefTotals operator +(BriefTotals left, BriefTotals right) => new(
        left.ActualHours + right.ActualHours,
        left.PlannedHours + right.PlannedHours,
        left.EntryCount + right.EntryCount,
        left.PeopleCount + right.PeopleCount);
}

/// <param name="Own">What people attached directly to this node did.</param>
/// <param name="Subtree">
/// <paramref name="Own"/> plus every descendant's. The rollup invariant is that this equals Own plus the sum of
/// the children's Subtree — which holds by construction because both are folded from the same per-node slices.
/// </param>
public sealed record NodeBriefBlock(
    Guid NodeId,
    Guid? ParentId,
    int LevelNo,
    string Code,
    string Name,
    BriefTotals Own,
    BriefTotals Subtree,
    IReadOnlyList<NodeBriefBlock> Children);

public sealed record NodeBriefView(
    Guid NodeId,
    ReportPeriodView Period,
    string Depth,
    NodeBriefBlock Node);
