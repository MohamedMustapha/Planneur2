using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Directory.Domain;

/// <summary>
/// The inheritance walk, as a pure function over an ancestry chain.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the service that loads the chain so the normative rule from v2 §10.1 can be tested without a
/// database, and so the rule has exactly one implementation. Both are the same point: the walk is where this slice
/// is either right or wrong, and it should not be reachable only through EF.
/// </para>
/// <para>
/// The chain is ordered self first, root last. Nothing in here knows how deep it is or what the levels are called
/// — a three-level deployment and a six-level one hand it the same shape, which is the property v2 §10 exists to
/// protect.
/// </para>
/// </remarks>
public static class NodeProfileResolution
{
    /// <summary>
    /// Resolves the profile in force, field by field.
    /// </summary>
    /// <param name="chain">
    /// Profiles attached along the ancestry, nearest first. Nodes with no profile attached contribute nothing and
    /// should simply be absent — a skipped level is not a gap, it is a level that had nothing to say.
    /// </param>
    /// <remarks>
    /// Two rules, and they are not the same rule:
    /// <list type="bullet">
    /// <item><description>
    /// <c>effective_profile(node)</c> is the nearest attached profile — that is where <see
    /// cref="NodeProfileSnapshot.SourceCode"/> and the label come from, so an administrator sees the profile they
    /// attached rather than a composite name for something that exists nowhere.
    /// </description></item>
    /// <item><description>
    /// <c>effective_field(node, f)</c> is the first non-null <c>f</c> walking self to root. This is what makes an
    /// override partial: a node can attach a profile that sets only the board and still inherit everything else,
    /// instead of having to restate its ancestor's taxonomy to change one field.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static NodeProfileSnapshot? Resolve(IReadOnlyList<NodeProfile> chain)
    {
        if (chain.Count == 0)
        {
            return null;
        }

        var nearest = chain[0];

        return new NodeProfileSnapshot(
            nearest.Code,
            nearest.LabelKey,
            FirstNonNull(chain, profile => profile.ActivityTaxonomyJson) ?? "{}",
            FirstNonNull(chain, profile => profile.BoardArchetypes) ?? [],
            FirstNonNull(chain, profile => profile.ItemTypes) ?? [],
            NodeCapabilities.Resolve(FirstNonNull(chain, profile => profile.CapabilitiesJson)),
            FirstNonNull(chain, profile => profile.SolvesCategories) ?? [],
            FirstNonNull(chain, profile => profile.BudgetDefaultsJson) ?? "{}",
            FirstNonNull(chain, profile => profile.HeadlinePattern));
    }

    private static T? FirstNonNull<T>(IReadOnlyList<NodeProfile> chain, Func<NodeProfile, T?> field)
        where T : class
    {
        foreach (var profile in chain)
        {
            if (field(profile) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
