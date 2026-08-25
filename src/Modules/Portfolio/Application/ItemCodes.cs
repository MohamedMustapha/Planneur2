using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Portfolio.Application;

/// <summary>
/// Allocates the stable short code every portfolio item carries (v2 §03.1).
/// </summary>
/// <remarks>
/// <para>
/// Shared by both ways an item comes into being — the catalog wizard and "propose a candidate" — because the code
/// is a property of the item, not of the screen that created it. Keeping the derivation in one place is what stops
/// the two paths drifting: the candidate path was written before items had codes, went on inserting an empty one,
/// and the unique index caught it on the second candidate anybody proposed.
/// </para>
/// <para>
/// Generated rather than asked for, per §03.3: the wizard asks for a name, and a required code field is exactly
/// the one that makes somebody abandon the form. The suffix only appears on a collision, so the ordinary case
/// reads as the word people already say out loud.
/// </para>
/// </remarks>
public static class ItemCodes
{
    private const int MaxStem = 12;

    private const int MaxAttempts = 100;

    /// <summary>
    /// The code the author gave, or one derived from the name and made unique.
    /// </summary>
    public static async Task<string> AllocateAsync(
        ICatalogReader catalog,
        string? requested,
        string name,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (requested is { Length: > 0 })
        {
            return requested;
        }

        var stem = Stem(name);

        if (!await catalog.CodeExistsAsync(stem, ct))
        {
            return stem;
        }

        for (var suffix = 2; suffix < MaxAttempts; suffix++)
        {
            var candidate = $"{stem}-{suffix}";

            if (!await catalog.CodeExistsAsync(candidate, ct))
            {
                return candidate;
            }
        }

        throw new DomainRuleViolationException($"Too many items already share the code {stem}. Give one yourself.");
    }

    /// <summary>The letters and digits of the name, upper-cased and cut to something sayable.</summary>
    public static string Stem(string name)
    {
        var stem = new string([.. (name ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit)]);

        return stem.Length switch
        {
            0 => "ITEM",
            > MaxStem => stem[..MaxStem],
            _ => stem,
        };
    }
}
