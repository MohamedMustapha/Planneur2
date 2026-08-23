using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Directory.Domain;

/// <summary>
/// A free-standing bundle of behaviour that any node may point at, and that every node below it inherits.
/// </summary>
/// <remarks>
/// <para>
/// v2 §10 corrects the earlier archetype design, which keyed behaviour off an organizational <em>kind</em>
/// (<c>it</c>, <c>business</c>, <c>intelligence</c>…). That repeats the mistake of naming the levels: it assumes
/// the deployment's own semantics, so a deployment whose org does not decompose that way has to be represented in
/// somebody else's vocabulary. A node therefore does not have a kind. It points at a profile, or it inherits the
/// nearest ancestor's, and the profile is a data row an administrator authors.
/// </para>
/// <para>
/// Every behavioural field is nullable, and that is the whole of per-field override: a node may attach a profile
/// that says nothing but "use the work-order board" and still inherit its parent's taxonomy, capabilities and
/// budget treatment. See <see cref="Services.NodeProfileResolver"/> for the walk.
/// </para>
/// </remarks>
public sealed class NodeProfile
{
    public required Guid Id { get; init; }

    /// <summary>
    /// The deployment's own word for this profile — <c>DELIVERY</c>, <c>CASEWORK</c>, <c>ADVISORY</c>.
    /// </summary>
    /// <remarks>
    /// Stable and unique, but deliberately not meaningful to the platform: an architecture test asserts that no
    /// code anywhere branches on it. It exists so an administrator can recognise a profile in a picker and so the
    /// seed can be re-run idempotently, not so a module can ask "is this the delivery one".
    /// </remarks>
    public required string Code { get; set; }

    /// <summary>Transloco key. Profile names are shown to users, so the DB stores the key (conventions.md §5).</summary>
    public required string LabelKey { get; set; }

    /// <summary>
    /// Buckets to subtypes, in the shape <see cref="Activities.Domain.ActivityTaxonomy"/> parses. Null inherits.
    /// </summary>
    /// <remarks>
    /// Only the subtypes are profile data. The four top buckets are fixed platform-wide and are merged in by the
    /// taxonomy itself, because an L1 head's brief stacks a delivery branch's incidents and a casework branch's
    /// processed files under the same four headings — which is only meaningful if both branches mean the same
    /// thing by them.
    /// </remarks>
    public string? ActivityTaxonomyJson { get; set; }

    /// <summary>
    /// Which board archetypes this branch may render, most-preferred first. Null inherits.
    /// </summary>
    /// <remarks>
    /// Per profile, not per level: an L3 doing dispatch gets the work-order board and its sibling doing delivery
    /// gets task progress, both under the same L2, without either of them knowing what level they are.
    /// </remarks>
    public string[]? BoardArchetypes { get; set; }

    /// <summary>Portfolio item types this branch may create (v2 §03). Null inherits.</summary>
    public string[]? ItemTypes { get; set; }

    /// <summary>
    /// Feature switches, as a JSON object of capability code to boolean. Null inherits.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="NodeCapabilities"/> rather than directly, so an unknown or absent key resolves to
    /// the registered default instead of to whatever <c>TryGetProperty</c> happened to return.
    /// </remarks>
    public string? CapabilitiesJson { get; set; }

    /// <summary>Problem categories this branch accepts org-wide (v2 §05). Null inherits.</summary>
    public string[]? SolvesCategories { get; set; }

    /// <summary>Capex/opex treatment per bucket (v2 §04). Null inherits.</summary>
    public string? BudgetDefaultsJson { get; set; }

    /// <summary>
    /// The template the report brief's headline sentence is rendered from. Null inherits.
    /// </summary>
    /// <remarks>
    /// Rendered against counts computed in code — the LLM receives the resolved vocabulary as a hint and writes
    /// only connective narrative. S8's rule that every number is computed is unchanged by this slice.
    /// </remarks>
    public string? HeadlinePattern { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public string? ModifiedBy { get; set; }
}
