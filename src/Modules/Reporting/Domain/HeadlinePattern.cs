using System.Globalization;
using System.Text.RegularExpressions;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// Renders a node profile's headline sentence from figures computed in code (v2 §10.5).
/// </summary>
/// <remarks>
/// <para>
/// The pattern is a deployment's own sentence — <c>"{people} pers · ~{hours} h · {c1} {c1_label} · …"</c> — and
/// the tokens are filled from counts this module worked out. The model never sees the pattern as something to
/// complete; it receives the rendered line as a fact and writes prose around it. That keeps S8's rule intact: the
/// vocabulary became configurable in this slice, the arithmetic did not.
/// </para>
/// <para>
/// Pure and static, like <see cref="SummaryPrompt"/>, and for the same reason — a template renderer's failures are
/// silent. A token that quietly rendered as empty produces a headline reading "· 12 h ·" that nobody notices is
/// missing half its content.
/// </para>
/// </remarks>
public static partial class HeadlinePattern
{
    /// <summary>
    /// A token left in the pattern that nothing supplied.
    /// </summary>
    /// <remarks>
    /// Rendered as an em dash rather than dropped or left as <c>{c2}</c>. Dropping it would produce a sentence
    /// with a hole in it that reads as though the figure were zero; leaving the brace shows an administrator
    /// their own typo, but shows it to every reader of the report as well. The dash says "nothing here" in a way
    /// that is true and legible.
    /// </remarks>
    public const string Missing = "—";

    /// <summary>
    /// Fills the tokens a profile's pattern names.
    /// </summary>
    /// <param name="pattern">
    /// The profile's <c>headline_pattern</c>. Null or blank returns null — a deployment that configured no
    /// sentence gets no headline, not an empty one, so the report falls back to its own opening line.
    /// </param>
    /// <param name="values">
    /// Token name to rendered value, without braces. Matching is case-insensitive because an administrator
    /// typing <c>{People}</c> meant the same thing as <c>{people}</c> and telling them otherwise is pedantry.
    /// </param>
    public static string? Render(string? pattern, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        return Token().Replace(
            pattern,
            match => lookup.TryGetValue(match.Groups["name"].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : Missing);
    }

    /// <summary>
    /// The tokens v2 §10.5 names, filled from one scope's figures.
    /// </summary>
    /// <param name="peopleCount">How many people the report covers.</param>
    /// <param name="hours">Total hours logged in the period.</param>
    /// <param name="leadingSubtypes">
    /// The subtypes that carried the most hours, largest first, each with the label key it renders through.
    /// </param>
    /// <param name="risks">How many risks the period surfaced.</param>
    /// <remarks>
    /// <c>c1</c> and <c>c2</c> bind to the two busiest subtypes rather than to fixed codes, which is what lets one
    /// sentence serve a delivery branch reporting incidents and a casework branch reporting processed files. A
    /// branch with only one subtype in play leaves <c>c2</c> unsupplied, and the renderer dashes it.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Values(
        int peopleCount,
        decimal hours,
        IReadOnlyList<(string Code, string LabelKey, decimal Hours)> leadingSubtypes,
        int risks)
    {
        ArgumentNullException.ThrowIfNull(leadingSubtypes);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["people"] = peopleCount.ToString(CultureInfo.InvariantCulture),
            ["hours"] = Math.Round(hours, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
            ["risks"] = risks.ToString(CultureInfo.InvariantCulture),
        };

        for (var index = 0; index < leadingSubtypes.Count && index < 2; index++)
        {
            var (_, labelKey, subtypeHours) = leadingSubtypes[index];
            var slot = index + 1;

            values[$"c{slot}"] = Math.Round(subtypeHours, 0, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture);

            // The label key, not a translation. The report is rendered in the reader's language by the client,
            // and resolving it here would bake one language into a stored figure.
            values[$"c{slot}_label"] = labelKey;
        }

        return values;
    }

    [GeneratedRegex(@"\{(?<name>[a-zA-Z0-9_]+)\}")]
    private static partial Regex Token();
}
