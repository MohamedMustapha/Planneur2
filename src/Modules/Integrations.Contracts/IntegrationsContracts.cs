using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Integrations.Contracts;

// =================================================================================================================
// The Integrations module's public surface. Other modules may reference this assembly and nothing else of
// Integrations' (architecture.md §2). Everything here is additive-only once released: another module is
// deserializing these from outbox rows written before the current deploy.
//
// Read-only is the shape of this file, not merely its documentation. There is no command, no write DTO and no
// method that could accept one. S10's single rule — the platform never pushes state back into a ticketing system —
// is expressible here as an absence, and an absence cannot be worked around by a caller in a hurry.
// =================================================================================================================

/// <summary>The external systems the platform mirrors. Stable codes, never localized (conventions.md §5).</summary>
public static class ExternalProviders
{
    /// <summary>BUILD work: sprint tasks and user stories for developers.</summary>
    public const string AzureDevOps = "azure-devops";

    /// <summary>RUN work: incidents and requests for the helpdesk.</summary>
    public const string ServiceNow = "servicenow";

    public static readonly IReadOnlyList<string> All = [AzureDevOps, ServiceNow];

    public static bool IsKnown(string? provider) =>
        provider is not null && All.Contains(provider, StringComparer.Ordinal);
}

/// <summary>
/// Whether the mirror still believes the item is live.
/// </summary>
/// <remarks>
/// A mirrored row is never deleted when it leaves the source query. An activity logged last month references it
/// by <c>external_ref</c>, and a report that cannot name the ticket somebody worked on is worse than a report
/// that names a closed one. Closing is therefore what "gone from the source" means here.
/// </remarks>
public static class MirrorStates
{
    /// <summary>The last sync of its connection still saw it.</summary>
    public const string Open = "open";

    /// <summary>The last sync of its connection did not. Kept, hidden from the feeds.</summary>
    public const string Closed = "closed";
}

/// <summary>
/// One mirrored work item, as the rest of the platform sees it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProjectId"/> and <see cref="UnitId"/> are the platform's own ids, derived at sync time through the
/// connection's mappings. They are on the row rather than resolved on read because they are what RLS filters on:
/// a predicate that had to walk a mapping table per candidate row would turn every dropdown into a join.
/// </para>
/// <para>
/// <see cref="ExternalId"/> is the source system's identity — a DevOps work item id, a ServiceNow sys_id. It is
/// unique within the connection that produced it and nowhere else: work item 4301 exists in every DevOps
/// collection ever created. It stays stable across syncs; nothing else about the row is promised to.
/// </para>
/// </remarks>
public sealed record ExternalWorkItemView(
    Guid Id,
    string Provider,
    string ExternalId,

    /// <summary>Human reference the source shows its own users — <c>AB#4312</c>, <c>INC0010023</c>.</summary>
    string Reference,
    string Title,

    /// <summary>The source's own type name: <c>Task</c>, <c>Bug</c>, <c>Incident</c>. Rendered as-is.</summary>
    string Type,

    /// <summary>The source's own state: <c>Active</c>, <c>In Progress</c>. Rendered as-is.</summary>
    string State,

    /// <summary>The sprint, iteration or assignment group the item sits in.</summary>
    string? SprintOrQueue,
    Guid? ProjectId,
    Guid? UnitId,
    Guid? AssignedPersonId,
    string? Url,
    decimal? EstimatedHours,
    DateTimeOffset? UpdatedAtSource,
    DateTimeOffset SyncedAt,
    string MirrorState);

/// <summary>
/// What a consumer is asking the mirror for.
/// </summary>
/// <remarks>
/// A record rather than a parameter list because the two consumers ask genuinely different questions of the same
/// table — S5 wants "mine, on the current sprint", S6a wants "nobody's, in this unit's queue" — and a third one
/// will ask a third. Adding a field here does not change either caller's code.
///
/// Note what it cannot express: another person's items. There is no <c>PersonId</c> field, because handing one
/// person another person's assigned tickets is a disclosure the external system never agreed to. "Assigned to
/// me" means the caller, resolved from the session.
/// </remarks>
public sealed record ExternalWorkItemQuery
{
    /// <summary>One provider, or every configured one when null.</summary>
    public string? Provider { get; init; }

    /// <summary>
    /// One connection's own items.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="Provider"/> and needed because it is not the same question: an organization may
    /// have two DevOps connections, and "what did this one bring back" is what an administrator looking at a
    /// connection is asking. Filtering by provider there would answer with both, and the two would be
    /// indistinguishable on screen.
    /// </remarks>
    public Guid? ConnectionId { get; init; }

    /// <summary>Only items resolved to the calling person.</summary>
    public bool AssignedToMe { get; init; }

    /// <summary>Only items nobody is assigned to — the 6a pool's question.</summary>
    public bool Unassigned { get; init; }

    /// <summary>Only items in the sprint or queue their connection currently points at.</summary>
    public bool CurrentSprint { get; init; }

    /// <summary>Narrow to one unit's queue. RLS still decides whether that unit is visible at all.</summary>
    public Guid? UnitId { get; init; }

    public Guid? ProjectId { get; init; }

    /// <summary>Include items the source has stopped returning. Off by default: the feeds want live work.</summary>
    public bool IncludeClosed { get; init; }

    public int Limit { get; init; } = 200;
}

/// <summary>
/// The mirror, as other modules read it.
/// </summary>
/// <remarks>
/// Every call runs under the caller's RLS session, so a consumer can only see what the matrix already allowed —
/// a developer their own DevOps items and their projects' sprints, a helpdesk agent their unit's queue. The
/// module does not re-implement any of that in application code, and could not: the mirror is a table.
/// </remarks>
public interface IExternalWorkItemReader
{
    Task<IReadOnlyList<ExternalWorkItemView>> QueryAsync(ExternalWorkItemQuery query, CancellationToken ct);
}

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// Raised after a connection finishes a pull that changed something.
/// </summary>
/// <remarks>
/// Carries counts rather than items. A consumer that wants the items reads them through
/// <see cref="IExternalWorkItemReader"/> under its own scope; putting them in the payload would mean an outbox row
/// containing work items nobody has checked the reader may see.
/// </remarks>
public sealed record ExternalWorkItemsSynced(
    Guid ConnectionId,
    string Provider,
    Guid DepartmentId,
    int Created,
    int Updated,
    int Closed) : IntegrationEvent;
