using Cracra.Modules.Integrations.Contracts;

namespace Cracra.Modules.Integrations.Domain;

// =================================================================================================================
// Three tables and no aggregate. Integrations is a 2-layer module (conventions.md §2): it is a connection you
// configure, a mapping you declare and a mirror a job keeps up to date. There is no lifecycle here, no invariant
// spanning entities, and nothing a state machine would clarify — which is exactly the decision rule for not
// reaching for the DDD archetype.
// =================================================================================================================

/// <summary>
/// One configured link to an external system.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to a department rather than to the organization, because that is who owns the relationship: the IS
/// department's DevOps project and the helpdesk's ServiceNow queue are configured, and paid for, by different
/// people. It is also what lets the config reuse the RLS predicate department settings already use, rather than
/// introducing a second answer to "who administers a department".
/// </para>
/// <para>
/// Credentials are not here. <see cref="AuthRef"/> names a secret the deployment holds — see
/// <c>Services/IntegrationCredentials.cs</c> — so that a row a department head may read, edit and export never
/// contains a token that would let them read the whole DevOps organization.
/// </para>
/// </remarks>
public sealed class ExternalConnection
{
    public required Guid Id { get; init; }

    /// <summary>
    /// The branch this connection is wired at. Inherited downward, so a branch can bring its own source without
    /// touching a sibling's (v2 00 3).
    /// </summary>
    public required Guid NodeId { get; set; }

    /// <summary>Kept for the shim's sake and written by nobody. Falls away with the legacy tables.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>azure-devops | servicenow. See <see cref="ExternalProviders"/>.</summary>
    public required string Provider { get; set; }

    /// <summary>What an administrator calls it in the list. Free text; there may well be two DevOps projects.</summary>
    public required string Name { get; set; }

    /// <summary>Collection or instance root, e.g. <c>https://devops.intranet/DefaultCollection</c>.</summary>
    public required string BaseUrl { get; set; }

    /// <summary>The name of the secret this connection authenticates with, never the secret itself.</summary>
    public required string AuthRef { get; set; }

    /// <summary>DevOps team project, or ServiceNow assignment group. What the pull is scoped to at the source.</summary>
    public required string ProjectOrQueue { get; set; }

    /// <summary>
    /// The iteration or sprint that "current" means for this connection.
    /// </summary>
    /// <remarks>
    /// Stated explicitly rather than asked of the provider. DevOps can answer <c>@currentIteration</c> itself,
    /// ServiceNow has no equivalent at all, and a feed whose meaning depends on which provider answered is a feed
    /// nobody can reason about. Null means this connection has no notion of a current sprint, and a query asking
    /// for one simply matches none of its items.
    /// </remarks>
    public string? CurrentSprint { get; set; }

    /// <summary>How often the scheduled pull runs. Zero disables the schedule; on-demand still works.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    public bool Active { get; set; } = true;

    // --- Sync state ----------------------------------------------------------------------------------------------
    // On the connection rather than in a run-history table. What an administrator needs is "did the last pull
    // work, when, and how much did it see"; a full history would be a log, and the logs are already in SEQ.

    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>never | ok | failed. Rendered as a badge; kept as a code, never as localized text.</summary>
    public string LastSyncStatus { get; set; } = SyncStatuses.Never;

    /// <summary>The provider's own failure message, truncated. Shown to whoever configured the connection.</summary>
    public string? LastSyncError { get; set; }

    /// <summary>Items the last successful pull returned, so "0" is distinguishable from "never ran".</summary>
    public int LastSyncItemCount { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public ICollection<ExternalMapping> Mappings { get; } = [];

    /// <summary>True when the schedule is due to pull this connection again.</summary>
    public bool IsDue(DateTimeOffset now) =>
        Active
        && PollInterval > TimeSpan.Zero
        && (LastSyncedAt is null || now - LastSyncedAt.Value >= PollInterval);
}

public static class SyncStatuses
{
    public const string Never = "never";
    public const string Ok = "ok";
    public const string Failed = "failed";
}

/// <summary>
/// How a value in the external system's vocabulary becomes a local project or unit.
/// </summary>
/// <remarks>
/// <para>
/// One table with a kind rather than the two the spec sketches. The two would have identical columns, identical
/// policies and identical endpoints, and every screen and every query would have to ask both — the discriminator
/// is less machinery for the same expressiveness, and a third vocabulary later is a new code rather than a new
/// table.
/// </para>
/// <para>
/// A check constraint ties the kind to which target may be set, so "an area path mapped to a unit" is refused by
/// the database rather than merely discouraged by whoever wrote the form.
/// </para>
/// </remarks>
public sealed class ExternalMapping
{
    public required Guid Id { get; init; }

    public required Guid ConnectionId { get; init; }

    /// <summary>Copied from the connection: RLS reads it, and a predicate should not need a join to answer.</summary>
    public required Guid NodeId { get; set; }

    /// <summary>Kept for the shim's sake and written by nobody. Falls away with the legacy tables.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>area-path | iteration | assignment-group. See <see cref="MappingKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>The source's own value, verbatim: an area path, an iteration name, an assignment group.</summary>
    public required string ExternalValue { get; set; }

    /// <summary>Set for area-path and iteration mappings.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Set for assignment-group mappings.</summary>
    public Guid? UnitId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ExternalConnection? Connection { get; set; }
}

public static class MappingKinds
{
    /// <summary>A DevOps area path to a local project.</summary>
    public const string AreaPath = "area-path";

    /// <summary>A DevOps iteration to a local project, for teams that organize by iteration rather than area.</summary>
    public const string Iteration = "iteration";

    /// <summary>A ServiceNow assignment group to a local unit.</summary>
    public const string AssignmentGroup = "assignment-group";

    public static readonly IReadOnlyList<string> All = [AreaPath, Iteration, AssignmentGroup];

    /// <summary>True where this kind targets a project; the rest target a unit.</summary>
    public static bool TargetsProject(string kind) =>
        string.Equals(kind, AreaPath, StringComparison.Ordinal)
        || string.Equals(kind, Iteration, StringComparison.Ordinal);
}

/// <summary>
/// The read-only mirror of one external work item.
/// </summary>
/// <remarks>
/// <para>
/// A row, not a pass-through. Every alternative — querying DevOps when the dropdown opens, holding responses in
/// memory — makes the feed as available as the external system and as authorized as whatever credential the
/// adapter happens to hold. Mirroring makes it as available as Postgres and as authorized as RLS, which is the
/// same answer every other feed in the platform gives.
/// </para>
/// <para>
/// The local ids are stamped at sync time, under the system context, because that is the only context that can
/// see every department's mappings at once. Nothing about them is re-derived on read.
/// </para>
/// </remarks>
public sealed class ExternalWorkItem
{
    public required Guid Id { get; init; }

    public required Guid ConnectionId { get; set; }

    public required string Provider { get; set; }

    /// <summary>The source system's identity. Unique within its connection; the upsert key.</summary>
    public required string ExternalId { get; set; }

    /// <summary>The reference the source shows its own users, and what an activity's external_ref holds.</summary>
    public required string Reference { get; set; }

    public required string Title { get; set; }

    /// <summary>The source's own type name: Task, Bug, Incident. Rendered as-is, never mapped to ours.</summary>
    public required string Type { get; set; }

    /// <summary>The source's own state. Rendered as-is: it is their word for it, and ours would be a guess.</summary>
    public required string State { get; set; }

    /// <summary>The LDAP uid the source reported, kept even when it resolves to nobody here.</summary>
    public string? AssignedToLdapUid { get; set; }

    /// <summary>The person that uid resolved to, if any. What "assigned to me" and RLS both read.</summary>
    public Guid? AssignedPersonId { get; set; }

    public string? SprintOrQueue { get; set; }

    /// <summary>
    /// True where <see cref="SprintOrQueue"/> matched its connection's current sprint at the last pull.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Denormalized rather than joined, and not for speed. The connection row is readable only by whoever
    /// administers the department — it names a supplier, a queue and a secret reference — so a "current sprint"
    /// filter expressed as a join to it would return nothing at all for the developers it exists to serve. The
    /// flag is the fact without the row.
    /// </para>
    /// <para>
    /// It is as fresh as the last pull. Moving a connection to the next sprint therefore takes effect on its next
    /// pull rather than instantly, which is the same latency every other fact on this row already has.
    /// </para>
    /// </remarks>
    public bool IsCurrentSprint { get; set; }

    /// <summary>Derived through the connection's mappings. Null where nothing maps — the item still mirrors.</summary>
    public Guid? ProjectId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>The connection's branch. The backstop scope: a head sees their branch's items.</summary>
    public required Guid NodeId { get; set; }

    /// <summary>Kept for the shim's sake and written by nobody. Falls away with the legacy tables.</summary>
    public Guid? DepartmentId { get; set; }

    public string? Url { get; set; }

    /// <summary>Remaining or original estimate where the source has one. Seeds the pool's estimated hours.</summary>
    public decimal? EstimatedHours { get; set; }

    public DateTimeOffset? UpdatedAtSource { get; set; }

    public DateTimeOffset SyncedAt { get; set; }

    /// <summary>open | closed. See <see cref="MirrorStates"/>.</summary>
    public string MirrorState { get; set; } = MirrorStates.Open;

    public DateTimeOffset? ClosedAt { get; set; }
}
