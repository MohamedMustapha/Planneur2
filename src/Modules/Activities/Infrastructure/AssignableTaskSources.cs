using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Integrations.Contracts;

namespace Cracra.Modules.Activities.Infrastructure;

/// <summary>
/// The dropdown, answered from S10's mirror.
/// </summary>
/// <remarks>
/// <para>
/// The seam S5 declared, now filled. What stands here used to be a pair of stand-ins — one that returned nothing
/// and one that invented plausible tasks for the dev box — and S10 replaces them rather than sitting behind them,
/// exactly as the note on the old file promised. A deployment with no DevOps connection now shows an empty
/// dropdown because the mirror is empty, which is the same honest answer arrived at for a better reason.
/// </para>
/// <para>
/// Nothing is fetched from Azure DevOps or ServiceNow here. The mirror is a table, so opening the dropdown is one
/// indexed query against Postgres under the caller's own RLS session — not a round trip whose latency and
/// availability belong to somebody else's server, and not a credential this module would otherwise have to hold.
/// </para>
/// </remarks>
internal sealed class MirrorTaskSource(
    string source,
    string suggestedTypeCode,
    IExternalWorkItemReader mirror,
    IUserContext user) : IAssignableTaskSource
{
    public string Source => source;

    public async Task<IReadOnlyList<AssignableTask>> GetAssignableAsync(Guid personId, CancellationToken ct)
    {
        // The port takes a person and the mirror answers for the session. They are the same person by
        // construction — S5's endpoint resolves the caller and passes them — and this is what keeps that true if
        // a future caller ever forgets: an empty list, rather than somebody else's assigned tickets.
        if (personId != user.UserId)
        {
            return [];
        }

        // S5 asks for two things: "assigned to me" and "on the current sprint". They are different questions —
        // an unassigned task on this sprint is available work, and a task assigned to me from three sprints ago
        // is still mine — so both are asked and the answers merged, rather than one being approximated with the
        // other. RLS answers the second: the sprint items a caller can see are the ones on their projects.
        var mine = await mirror.QueryAsync(
            new ExternalWorkItemQuery { Provider = source, AssignedToMe = true },
            ct);

        var sprint = await mirror.QueryAsync(
            new ExternalWorkItemQuery { Provider = source, CurrentSprint = true },
            ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tasks = new List<AssignableTask>(mine.Count + sprint.Count);

        foreach (var item in mine.Concat(sprint))
        {
            // On the reference, not the external id: an id is only unique inside the connection that issued it,
            // so two connections covering the same DevOps project would otherwise offer the same task twice —
            // and the reference is what lands in the entry either way.
            if (!seen.Add(item.Reference))
            {
                continue;
            }

            tasks.Add(new AssignableTask(
                item.Provider,

                // The reference the source shows its own users — AB#4312, INC0010023 — rather than the internal
                // id. It is what lands in the entry's external_ref, and what somebody reading a report a year
                // later has to be able to paste into the other system's search box.
                item.Reference,
                item.Title,
                item.State,
                item.ProjectId,
                suggestedTypeCode));
        }

        return tasks;
    }
}

/// <summary>Wires one source per provider.</summary>
/// <remarks>
/// Two registrations of one class rather than two classes: the difference between them is a provider code and
/// which activity type a pulled task pre-fills, and neither is behaviour. BUILD work suggests project-build, RUN
/// work suggests project-run — the taxonomy's own split, applied at the point a task becomes an entry.
/// </remarks>
internal static class AssignableTaskRegistration
{
    public static IReadOnlyList<IAssignableTaskSource> Build(IExternalWorkItemReader mirror, IUserContext user) =>
    [
        new MirrorTaskSource(ExternalProviders.AzureDevOps, "project-build", mirror, user),
        new MirrorTaskSource(ExternalProviders.ServiceNow, "project-run", mirror, user),
    ];
}
