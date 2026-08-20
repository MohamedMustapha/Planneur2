using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;

namespace Cracra.Modules.Integrations.Sync;

/// <summary>
/// Turning what a provider found into what the mirror stores.
/// </summary>
/// <remarks>
/// Static and pure, with no clock and no database, because this is the part of sync that is worth testing
/// exhaustively and the part that has nothing to do with I/O. Everything time-dependent is passed in.
/// </remarks>
internal static class MirrorMapper
{
    /// <summary>
    /// Which local project or unit a snapshot belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hints are offered in the provider's order of confidence and the first that matches a mapping wins. That
    /// is what makes a team mapping both an area path and an iteration get the area: the DevOps adapter offers
    /// the area first, because an area outlives the fortnight an iteration is named for.
    /// </para>
    /// <para>
    /// Nothing matching is not an error. An item in an unmapped area still mirrors, still shows up for the
    /// person it is assigned to, and simply has no project — which is honest. Refusing to mirror it would mean a
    /// developer's own assigned task vanishing from their dropdown because somebody had not finished the mapping
    /// table, and the failure would look like the integration being broken.
    /// </para>
    /// </remarks>
    public static (Guid? ProjectId, Guid? UnitId) ResolveTarget(
        IReadOnlyList<MappingHint> hints,
        IReadOnlyList<ExternalMapping> mappings)
    {
        foreach (var hint in hints)
        {
            var match = mappings.FirstOrDefault(mapping =>
                string.Equals(mapping.Kind, hint.Kind, StringComparison.Ordinal)
                && string.Equals(mapping.ExternalValue, hint.Value, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return (match.ProjectId, match.UnitId);
            }
        }

        return (null, null);
    }

    /// <summary>Creates the mirror row for a snapshot nothing has been seen for yet.</summary>
    public static ExternalWorkItem Create(
        ExternalWorkItemSnapshot snapshot,
        ExternalConnection connection,
        IReadOnlyList<ExternalMapping> mappings,
        IReadOnlyDictionary<string, Guid> people,
        DateTimeOffset now)
    {
        var item = new ExternalWorkItem
        {
            Id = Guid.CreateVersion7(),
            ConnectionId = connection.Id,
            Provider = connection.Provider,
            ExternalId = snapshot.ExternalId,
            Reference = snapshot.Reference,
            Title = snapshot.Title,
            Type = snapshot.Type,
            State = snapshot.State,
            DepartmentId = connection.DepartmentId,
        };

        Apply(item, snapshot, connection, mappings, people, now);

        return item;
    }

    /// <summary>
    /// Restamps an existing mirror row from a fresh snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is overwritten, including the target ids and the resolved person. The source is authoritative
    /// about all of it, and a field kept because "it was set once" is how a mirror starts disagreeing with what
    /// it mirrors — a ticket reassigned to somebody else would otherwise keep showing in the first agent's
    /// dropdown indefinitely.
    /// </para>
    /// <para>
    /// Seeing an item again also reopens it. An incident reopened at the source is live work again, and leaving
    /// it closed here would hide it from the very pool that is supposed to surface it.
    /// </para>
    /// </remarks>
    public static void Apply(
        ExternalWorkItem item,
        ExternalWorkItemSnapshot snapshot,
        ExternalConnection connection,
        IReadOnlyList<ExternalMapping> mappings,
        IReadOnlyDictionary<string, Guid> people,
        DateTimeOffset now)
    {
        var (projectId, unitId) = ResolveTarget(snapshot.MappingHints, mappings);

        item.ConnectionId = connection.Id;
        item.Provider = connection.Provider;
        item.Reference = snapshot.Reference;
        item.Title = snapshot.Title;
        item.Type = snapshot.Type;
        item.State = snapshot.State;
        item.AssignedToLdapUid = snapshot.AssignedToLdapUid;
        item.AssignedPersonId = ResolvePerson(snapshot.AssignedToLdapUid, people);
        item.SprintOrQueue = snapshot.SprintOrQueue;

        // Decided here, against the connection, because this is the only place both are in hand. Case-insensitive
        // for the same reason the mappings are: an administrator typing "sprint 42" into a settings field should
        // not silently produce a feed that is always empty.
        item.IsCurrentSprint = connection.CurrentSprint is { } current
                               && string.Equals(snapshot.SprintOrQueue, current, StringComparison.OrdinalIgnoreCase);

        item.ProjectId = projectId;

        // A ServiceNow item takes the unit its assignment group maps to; a DevOps item has none, and falls back
        // to nothing rather than to the connection's department's first unit. An item that claims to be in a
        // queue it was never in would put itself in front of a team that cannot do anything with it.
        item.UnitId = unitId;
        item.DepartmentId = connection.DepartmentId;
        item.Url = snapshot.Url;
        item.EstimatedHours = snapshot.EstimatedHours;
        item.UpdatedAtSource = snapshot.UpdatedAtSource;
        item.SyncedAt = now;
        item.MirrorState = MirrorStates.Open;
        item.ClosedAt = null;
    }

    /// <summary>
    /// The uid the source reported, matched against the directory.
    /// </summary>
    /// <remarks>
    /// An unresolved uid leaves <c>assigned_person_id</c> null and keeps the raw uid on the row. That is the
    /// case of a contractor with a DevOps account and no LDAP entry, and the honest result is an item that is
    /// visibly assigned to somebody the platform does not know — not one that looks unassigned and gets dragged
    /// out of the pool by a lead who thinks it is free.
    /// </remarks>
    private static Guid? ResolvePerson(string? ldapUid, IReadOnlyDictionary<string, Guid> people) =>
        ldapUid is not null && people.TryGetValue(ldapUid, out var personId) ? personId : null;
}
