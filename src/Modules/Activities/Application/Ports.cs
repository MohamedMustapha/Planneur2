using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Activities.Domain;

namespace Cracra.Modules.Activities.Application;

public interface IActivityRepository
{
    Task<ActivityEntry> GetAsync(Guid entryId, CancellationToken ct);

    Task AddAsync(ActivityEntry entry, CancellationToken ct);

    /// <summary>
    /// Deletes the entry, or refuses if the write policy will not have it.
    /// </summary>
    /// <remarks>
    /// Postgres filters a DELETE its RLS policy rejects rather than raising — the statement simply matches no
    /// rows. So the refusal has to be detected from the row count, which is unambiguous here: the caller has
    /// already read this row in the same transaction, so nothing but the write policy can make it vanish.
    /// </remarks>
    Task DeleteAsync(ActivityEntry entry, CancellationToken ct);

    /// <summary>Actual hours already recorded for a person's week, excluding one entry being amended.</summary>
    Task<decimal> RecordedHoursAsync(Guid personId, IsoWeek week, Guid? excludingEntryId, CancellationToken ct);

    /// <summary>
    /// The open planned slot a new actual should reconcile against, if there is one.
    /// </summary>
    /// <remarks>
    /// "In the window" means overlapping the actual's slot: the spec asks an actual to reference or supersede a
    /// plan when one exists in the window, and overlap is the only definition of that which survives someone
    /// starting an hour late.
    /// </remarks>
    Task<ActivityEntry?> FindOpenPlanAsync(Guid personId, TimeSlot slot, CancellationToken ct);
}

/// <summary>What Activities needs from the org, as a port.</summary>
public interface IDirectoryPort
{
    /// <summary>Where a person sits right now. The entry copies this onto the row for RLS.</summary>
    Task<(Guid UnitId, Guid DepartmentId)?> GetPlacementAsync(Guid personId, CancellationToken ct);

    /// <summary>The department's taxonomy and weekly target, merged over the canonical buckets.</summary>
    Task<DepartmentPolicy> GetPolicyAsync(Guid departmentId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(IReadOnlyList<Guid> personIds, CancellationToken ct);
}

/// <summary>The department knobs that govern one person's logging.</summary>
public sealed record DepartmentPolicy(ActivityTaxonomy Taxonomy, decimal WeeklyTargetHours, bool EnforceWeeklyTarget);

public interface IProjectsPort
{
    /// <summary>True when the person is an active member of the project.</summary>
    Task<bool> IsMemberAsync(Guid projectId, Guid personId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetProjectCodesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct);
}

/// <summary>
/// The read-only pull from Azure DevOps and ServiceNow.
/// </summary>
/// <remarks>
/// <para>
/// The seam S10 fills. Declared here because S5 owns what the dropdown is <em>for</em> — pre-filling an entry —
/// and only the shape it needs; how a sprint is queried is S10's problem entirely.
/// </para>
/// <para>
/// Read-only in the strong sense: there is no write method here and there will not be one. The platform never
/// pushes state back into a ticketing system, because the moment it does, two systems own the same fact.
/// </para>
/// </remarks>
public interface IAssignableTaskSource
{
    /// <summary>The source code this provider answers for: <c>azure-devops</c> or <c>servicenow</c>.</summary>
    string Source { get; }

    /// <summary>Tasks assigned to this person, or on their current sprint. Never anyone else's.</summary>
    Task<IReadOnlyList<AssignableTask>> GetAssignableAsync(Guid personId, CancellationToken ct);
}
