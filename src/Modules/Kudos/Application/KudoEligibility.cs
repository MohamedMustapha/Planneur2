using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Kudos.Contracts;
using Cracra.Modules.Kudos.Domain;

namespace Cracra.Modules.Kudos.Application;

/// <summary>
/// Who may recognise whom.
/// </summary>
/// <remarks>
/// <para>
/// One class answers both halves of the question — "may I thank this person" on the write, and "who may I thank"
/// for the picker — because they have to agree. A list built from one rule and a guard written from another is how
/// a form ends up offering somebody a colleague the server will then refuse.
/// </para>
/// <para>
/// This is not the visibility matrix restated. RLS decides which kudos come back; this decides whether an act is
/// permitted, which is a different question and one no row-level policy can answer, because the row does not exist
/// yet. The write policy on the table is the backstop: it refuses anything not authored by the caller, whatever
/// this concludes.
/// </para>
/// </remarks>
internal sealed class KudoEligibility(IDirectoryPort directory, IProjectsPort projects, IUserContext user)
{
    /// <summary>Why the caller may recognise this person, or <see cref="KudoRelation.None"/>.</summary>
    public async Task<KudoRelation> ResolveAsync(Guid receiverId, Guid receiverUnitId, Guid receiverDepartmentId, CancellationToken ct)
    {
        if (user.UnitId is { } unit && receiverUnitId == unit)
        {
            // The ordinary case, and the reason the matrix widens a member's activity feed to their whole unit in
            // the first place.
            return KudoRelation.UnitPeer;
        }

        if (user.Has(ContextualRole.Pmo)
            || (user.Has(ContextualRole.DepartmentHead) && user.DepartmentIds.Contains(receiverDepartmentId)))
        {
            return KudoRelation.HeadScope;
        }

        var peers = await projects.GetPeersAsync(user.UserId, ct);

        return peers.Contains(receiverId) ? KudoRelation.ProjectPeer : KudoRelation.None;
    }

    /// <summary>
    /// Everyone the caller may recognise.
    /// </summary>
    /// <remarks>
    /// Unit peers first, then project teammates, then — for a head — the rest of the scope they run. Unit and
    /// department names come from Directory under the caller's own session, because those lists are exactly where
    /// its own scoping is meant to apply. Project teammates do not: a contributor from another department has no
    /// name the caller can read, and dropping them here would offer a list narrower than what the write accepts —
    /// which is the disagreement this class exists to prevent.
    /// </remarks>
    public async Task<IReadOnlyList<EligiblePeer>> ListAsync(CancellationToken ct)
    {
        var found = new Dictionary<Guid, EligiblePeer>();

        foreach (var person in await directory.GetPeopleAsync(user.UnitId, null, ct))
        {
            if (person.Id != user.UserId && person.Active)
            {
                found[person.Id] = new EligiblePeer(person.Id, person.DisplayName, person.UnitId, UnitRelation);
            }
        }

        var peers = await projects.GetPeersAsync(user.UserId, ct);

        var missing = peers.Where(peer => !found.ContainsKey(peer) && peer != user.UserId).ToList();

        if (missing.Count > 0)
        {
            var names = await directory.GetTeammateNamesAsync(missing, ct);

            foreach (var (personId, name) in names)
            {
                found[personId] = new EligiblePeer(personId, name, null, ProjectRelation);
            }
        }

        if (user.Has(ContextualRole.DepartmentHead) || user.Has(ContextualRole.Pmo))
        {
            foreach (var departmentId in user.DepartmentIds)
            {
                foreach (var person in await directory.GetPeopleAsync(null, departmentId, ct))
                {
                    if (person.Id != user.UserId && person.Active && !found.ContainsKey(person.Id))
                    {
                        found[person.Id] = new EligiblePeer(
                            person.Id, person.DisplayName, person.UnitId, ScopeRelation);
                    }
                }
            }
        }

        return [.. found.Values.OrderBy(peer => peer.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public const string UnitRelation = "unit";
    public const string ProjectRelation = "project";
    public const string ScopeRelation = "scope";
}
