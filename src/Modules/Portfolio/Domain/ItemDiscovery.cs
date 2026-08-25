namespace Cracra.Modules.Portfolio.Domain;

/// <summary>
/// The org-wide discovery projection of a portfolio item (v2 §01 §3.1).
/// </summary>
/// <remarks>
/// <para>
/// The catalog exists so somebody can find out whether a thing already exists before asking for it to be built,
/// and a duplicate nobody outside the owning branch can see is a duplicate that gets built twice. So the spec makes
/// the narrow answer — what a thing is called, what it is, whose it is, where it has got to — readable by anyone
/// authenticated, while the identity card stays behind <c>can_read_item</c>.
/// </para>
/// <para>
/// It is a table rather than a wider policy because row-level security is exactly that: row-level. Loosening
/// <c>portfolio_item_read</c> enough for a stranger to see the name would hand them the estimate, the lead and the
/// decision notes in the same row. A second, deliberately impoverished row is the only way to say "this much and
/// no more" in a place the database can enforce.
/// </para>
/// <para>
/// Nothing writes it by hand. A trigger on <c>portfolio_item</c> keeps it in step, and its write policy admits only
/// the system scope — the same shape as <c>access.project_membership</c>, and for the same reason: a projection
/// somebody's own session can rewrite is a projection that decides what they are allowed to see.
/// </para>
/// </remarks>
public sealed class ItemDiscovery
{
    public Guid ItemId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public string Classification { get; private set; } = string.Empty;

    public string? Category { get; private set; }

    public Guid OwnerNodeId { get; private set; }

    public string State { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    /// <summary>Mirrored from the item so the read policy can answer without joining back to it.</summary>
    public bool Confidential { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
