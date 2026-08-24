namespace Cracra.Modules.Access.Domain;

/// <summary>
/// One administrative act, recorded as it happened (v2 §08.2).
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and that is the whole design. An audit row that can be edited answers a different question from
/// the one anybody asks it — "what does the record say now" rather than "what did somebody do" — so there is no
/// update policy on the table and nothing here has a setter.
/// </para>
/// <para>
/// It carries the node the act landed on, which is what lets a head read the trail for their own branch without
/// reading the whole organisation's. Global acts — creating a level, granting PMO — carry none, and only the
/// people who may perform them see those.
/// </para>
/// </remarks>
public sealed class AdminAuditEntry
{
    private AdminAuditEntry()
    {
    }

    public Guid Id { get; private init; }

    public Guid ActorPersonId { get; private init; }

    /// <summary>node-created / node-renamed / node-reparented / role-granted / … — <see cref="AdminActions"/>.</summary>
    public string Action { get; private init; } = string.Empty;

    public string TargetType { get; private init; } = string.Empty;

    public Guid TargetId { get; private init; }

    /// <summary>The node the act landed on, or null for something org-wide.</summary>
    public Guid? NodeId { get; private init; }

    /// <summary>What changed, in words. Rendered as-is: it is written for whoever reads the trail later.</summary>
    public string Detail { get; private init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private init; }

    public static AdminAuditEntry Of(
        Guid actorPersonId,
        string action,
        string targetType,
        Guid targetId,
        Guid? nodeId,
        string detail,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ActorPersonId = actorPersonId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            NodeId = nodeId,
            Detail = detail.Trim(),
            OccurredAt = now,
        };
}
