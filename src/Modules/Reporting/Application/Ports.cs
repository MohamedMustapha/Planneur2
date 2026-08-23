using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;
using Cracra.Modules.Scheduling.Contracts;

namespace Cracra.Modules.Reporting.Application;

// =================================================================================================================
// What Reporting needs from everywhere else.
//
// Six modules' worth of reads, each behind a port, each implemented by an adapter over that module's own contract.
// The indirection earns its keep here more than anywhere else in the system: without it the composer would name
// six other modules' DTOs directly and the report's shape would be the union of six other teams' decisions.
//
// Every one of these runs inside the caller's RLS session. That is not an implementation detail, it is the whole
// security model of this slice: a report is a projection over rows the viewer could already have read one at a
// time, so it cannot exceed their rights however the scopes are combined.
// =================================================================================================================

/// <summary>Activity, which is what most sections are ultimately counting.</summary>
public interface IActivityQueries
{
    Task<IReadOnlyList<ActivityEntryView>> ForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    Task<IReadOnlyList<ActivityEntryView>> ForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    /// <summary>The department's weekly target and whether it is enforced — the "35h status" line.</summary>
    Task<(decimal TargetHours, bool Enforced)> TargetAsync(Guid? departmentId, CancellationToken ct);

    /// <summary>Hours over a window, one row per node in a subtree. The brief's only source of numbers.</summary>
    Task<IReadOnlyList<NodeHoursSlice>> HoursByNodeAsync(
        Guid rootNodeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}

/// <summary>The org tree, for the one report whose shape is the tree itself.</summary>
public interface IOrgNodeQueries
{
    Task<IReadOnlyList<OrgNodeSummary>> SubtreeAsync(Guid nodeId, CancellationToken ct);
}

public interface IDirectoryQueries
{
    Task<PersonSummary?> PersonAsync(Guid personId, CancellationToken ct);

    Task<IReadOnlyList<PersonSummary>> PeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyList<UnitSummary>> UnitsAsync(Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> DepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);

    /// <summary>The node profile in force for the caller's branch (v2 §10), or null where none is attached.</summary>
    Task<NodeProfileSnapshot?> NodeProfileAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);
}

public interface IProjectQueries
{
    Task<ProjectSummary?> ProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>Every project the caller can read. RLS decides what that means; this does not filter further.</summary>
    Task<IReadOnlyList<ProjectSummary>> VisibleAsync(CancellationToken ct);

    Task<IReadOnlyList<ProjectTeamMemberView>> TeamAsync(Guid projectId, CancellationToken ct);
}

public interface IPortfolioQueries
{
    Task<PortfolioBoard> BoardAsync(CancellationToken ct);

    Task<IReadOnlyList<IterationSummary>> IterationsAsync(Guid projectId, CancellationToken ct);
}

public interface IMeetingQueries
{
    Task<IReadOnlyList<UpcomingEntry>> InWindowAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public interface IScheduleQueries
{
    Task<ScheduleLoad> LoadAsync(Guid unitId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// Kudos in the period — the seam S9 filled.
/// </summary>
/// <remarks>
/// Declared in S8 and answering nothing until S9 existed, exactly as S6 declared its calendar overlay source
/// before S7. The unit report already asked the question and already rendered whatever came back, so S9 cost one
/// registration rather than a change to the report's shape and its client template.
///
/// The alternative — leaving kudos out entirely and adding the section later — would have meant revisiting the
/// report contract, the PDF renderer and the Angular view at the point where S9's own schedule was tightest.
/// </remarks>
public interface IKudosQueries
{
    Task<int> CountAsync(Guid? unitId, Guid? departmentId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// The on-prem model, behind a port.
/// </summary>
/// <remarks>
/// The spec asks for this explicitly and an architecture test enforces it: nothing in Application or Domain names
/// the HTTP client. What it buys, concretely, is that the integration tests run the whole summary path against
/// the stub without a single conditional in production code.
/// </remarks>
public interface IAiSummarizer
{
    string Model { get; }

    Task<string> WriteAsync(SummaryRequest request, string language, CancellationToken ct);

    /// <summary>Token by token, for the panel that streams the narrative in as it is written.</summary>
    IAsyncEnumerable<string> StreamAsync(SummaryRequest request, string language, CancellationToken ct);
}

/// <summary>The cached narratives. A tiny repository over the module's one table.</summary>
public interface ISummaryStore
{
    /// <summary>The stored narrative for this exact prompt, or null. Cache hits are keyed on the figures too.</summary>
    Task<StoredSummary?> FindAsync(ReportDescriptor descriptor, string promptHash, CancellationToken ct);

    /// <summary>The most recent narrative for this report whatever the figures were — the "stale" case.</summary>
    Task<StoredSummary?> FindLatestAsync(ReportDescriptor descriptor, CancellationToken ct);

    Task<StoredSummary> SaveAsync(
        ReportDescriptor descriptor,
        string promptHash,
        string model,
        string text,
        int promptCharacters,
        CancellationToken ct);
}

public sealed record StoredSummary(
    Guid Id,
    string Text,
    string Model,
    string Language,
    string PromptHash,
    DateTimeOffset CreatedAt);

/// <summary>Renders a composed report to bytes. One implementation per format; today that means PDF.</summary>
public interface IReportRenderer
{
    string Format { get; }

    string ContentType { get; }

    byte[] Render(ReportView report);
}
