using System.Text.Json;
using System.Text.Json.Serialization;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Kudos.Domain;

/// <summary>
/// How far a department has turned recognition up.
/// </summary>
/// <remarks>
/// Ordered, and the order is the point: <see cref="Points"/> is <see cref="Counter"/> plus a number, and
/// <see cref="PointsBadgesLeaderboard"/> is that plus a ranking. Comparisons in the domain read
/// <c>mode >= KudoMode.Points</c> rather than enumerating cases, so adding a rung later does not mean revisiting
/// every question anybody asks of the mode.
/// </remarks>
public enum KudoMode
{
    Counter = 0,
    Points = 1,
    PointsBadgesLeaderboard = 2,
}

/// <summary>One category a department offers, and what a kudo in it is worth.</summary>
/// <param name="Code">Stable, never localized. Reports and the annual view group on this.</param>
/// <param name="LabelKey">Translation key. A department may relabel a canonical category without renaming it.</param>
/// <param name="Points">
/// What the category is worth. Recorded on every kudo whatever the mode — see <see cref="KudoRules"/>.
/// </param>
public sealed record KudoCategory(string Code, string LabelKey, int Points);

/// <summary>
/// What somebody has to accumulate to earn a badge.
/// </summary>
/// <remarks>
/// Three optional conditions, all of which must hold. A badge with none of them set would be earned by everybody
/// the moment they received their first kudo, so <see cref="IsMeaningful"/> refuses it at parse time rather than
/// letting a typo in department settings hand the whole unit a badge.
/// </remarks>
public sealed record BadgeCriteria(int? MinPoints, int? MinCount, string? Category)
{
    public bool IsMeaningful => MinPoints is > 0 || MinCount is > 0;
}

/// <summary>A badge a department offers, and the bar for it.</summary>
public sealed record BadgeDefinition(string Code, string LabelKey, BadgeCriteria Criteria);

/// <summary>
/// One department's recognition rules, merged over the platform's.
/// </summary>
/// <remarks>
/// <para>
/// Parsed from <c>DepartmentConfig.KudoRulesJson</c> (S1), exactly as S5 parses the activity taxonomy out of the
/// same row, and merged over a canonical set for the same reason: S8 counts kudos across departments, and it can
/// only do that if <c>cleanup</c> means the same thing everywhere. A department may relabel a canonical category,
/// reprice it, and add its own; it may not delete one.
/// </para>
/// <para>
/// Malformed configuration falls back to the canonical rules rather than throwing. One bad edit in department
/// settings should cost a department its customisation, not its ability to thank anybody.
/// </para>
/// <para>
/// <b>Points are always resolved, even in counter mode.</b> The spec describes them as zero there, and from
/// outside they are — <see cref="ShowsPoints"/> is false and no response carries a number. But the column keeps
/// what the category was worth, because the alternative is that a department switching points on gets a
/// leaderboard where everything before the settings change is worth nothing. A ranking that resets on an
/// administrative act is a ranking nobody believes, and the recovery — backfilling points from rules that have
/// since changed — is worse than recording them all along.
/// </para>
/// </remarks>
public sealed class KudoRules
{
    private readonly Dictionary<string, KudoCategory> _categories;

    private KudoRules(
        KudoMode mode,
        int monthlyCapPerGiver,
        IEnumerable<KudoCategory> categories,
        IReadOnlyList<BadgeDefinition> badges)
    {
        Mode = mode;
        MonthlyCapPerGiver = monthlyCapPerGiver;
        Badges = badges;
        _categories = categories.ToDictionary(category => category.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The categories every department inherits, and the platform's default prices.
    /// </summary>
    /// <remarks>
    /// The five the spec names. The prices exist so that a department which does nothing but flip the mode gets a
    /// working ladder rather than a leaderboard where everything is worth one; a department that disagrees with
    /// the scale says so in its own configuration, which is the knob the spec asks for.
    /// </remarks>
    public static readonly IReadOnlyList<KudoCategory> CanonicalCategories =
    [
        new("cleanup", "kudos.category.cleanup", 1),
        new("improvement", "kudos.category.improvement", 2),
        new("initiative", "kudos.category.initiative", 3),
        new("mentoring", "kudos.category.mentoring", 3),
        new("above-and-beyond", "kudos.category.above-and-beyond", 5),
    ];

    /// <summary>
    /// The ladder every department inherits.
    /// </summary>
    /// <remarks>
    /// Four rungs, spread far enough apart that they are not all reached in the same fortnight, and one of them
    /// keyed to a category rather than to a total — recognition for mentoring is the kind a review actually asks
    /// about, and a ladder made only of totals would flatten it into "was generally appreciated".
    /// </remarks>
    public static readonly IReadOnlyList<BadgeDefinition> CanonicalBadges =
    [
        new("first-kudo", "kudos.badge.first-kudo", new BadgeCriteria(null, 1, null)),
        new("helping-hand", "kudos.badge.helping-hand", new BadgeCriteria(10, null, null)),
        new("cornerstone", "kudos.badge.cornerstone", new BadgeCriteria(25, null, null)),
        new("mentor", "kudos.badge.mentor", new BadgeCriteria(null, 5, "mentoring")),
    ];

    /// <summary>
    /// How many kudos one person may give in a calendar month before the platform stops them.
    /// </summary>
    /// <remarks>
    /// Ten. Enough that nobody who is genuinely thanking colleagues ever meets it, low enough that a leaderboard
    /// cannot be manufactured by two people agreeing to praise each other all afternoon.
    /// </remarks>
    public const int DefaultMonthlyCapPerGiver = 10;

    /// <summary>The ceiling a department may configure. Above this the cap has stopped being one.</summary>
    public const int MaximumMonthlyCapPerGiver = 200;

    public KudoMode Mode { get; }

    public int MonthlyCapPerGiver { get; }

    public IReadOnlyList<BadgeDefinition> Badges { get; }

    public IReadOnlyCollection<KudoCategory> Categories => _categories.Values;

    /// <summary>True where a number may appear next to a kudo.</summary>
    public bool ShowsPoints => Mode >= KudoMode.Points;

    /// <summary>True where people may be ranked against each other.</summary>
    public bool ShowsLeaderboard => Mode >= KudoMode.PointsBadgesLeaderboard;

    /// <summary>
    /// Whether a department may let people recognise themselves.
    /// </summary>
    /// <remarks>
    /// A constant, not a knob, although the spec lists <c>self_kudo_allowed=false</c> among the configurable
    /// rules. Every other rule here trades off between departments that count and departments that score; this one
    /// has no second setting worth having. A department that switched it on would not have configured recognition
    /// differently, it would have removed the only thing that makes a count of it mean anything — and it would do
    /// so for everyone downstream, including S8's cross-department report.
    /// </remarks>
    public const bool SelfKudoAllowed = false;

    public static KudoRules Default => Resolve(null);

    /// <summary>Merges a department's configured rules over the platform's.</summary>
    public static KudoRules Resolve(string? configJson)
    {
        var configured = Parse(configJson);

        var categories = CanonicalCategories.ToDictionary(
            category => category.Code,
            StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in configured?.Categories ?? [])
        {
            if (string.IsNullOrWhiteSpace(candidate.Code))
            {
                continue;
            }

            var code = candidate.Code.Trim().ToLowerInvariant();
            var canonical = categories.GetValueOrDefault(code);

            categories[code] = new KudoCategory(
                code,
                string.IsNullOrWhiteSpace(candidate.LabelKey)
                    ? canonical?.LabelKey ?? $"kudos.category.{code}"
                    : candidate.LabelKey.Trim(),
                // Clamped rather than rejected: a negative price would let a department punish through the
                // recognition system, which is the one thing it must not be usable for.
                Math.Clamp(candidate.Points ?? canonical?.Points ?? 1, 0, MaximumCategoryPoints));
        }

        // Badges are replaced wholesale where a department defines any, unlike categories. Nothing outside the
        // department reports on badge codes, so a department that writes its own ladder is not dropping vocabulary
        // anybody else depends on — whereas one that merely wanted to raise a threshold would otherwise be stuck
        // with the old rung sitting next to the new one.
        var badges = configured?.Badges is { Count: > 0 } defined
            ?
            [
                .. defined
                    .Where(badge => !string.IsNullOrWhiteSpace(badge.Code))
                    .Select(badge => new BadgeDefinition(
                        badge.Code.Trim().ToLowerInvariant(),
                        string.IsNullOrWhiteSpace(badge.LabelKey)
                            ? $"kudos.badge.{badge.Code.Trim().ToLowerInvariant()}"
                            : badge.LabelKey.Trim(),
                        new BadgeCriteria(badge.MinPoints, badge.MinCount, badge.Category?.Trim().ToLowerInvariant())))
                    .Where(badge => badge.Criteria.IsMeaningful),
            ]
            : CanonicalBadges;

        return new KudoRules(
            ParseMode(configured?.Mode),
            configured?.MonthlyCapPerGiver is { } cap && cap > 0
                ? Math.Min(cap, MaximumMonthlyCapPerGiver)
                : DefaultMonthlyCapPerGiver,
            categories.Values,
            badges);
    }

    public bool Contains(string code) => _categories.ContainsKey(code);

    public KudoCategory Get(string code) =>
        _categories.TryGetValue(code, out var category)
            ? category
            : throw new DomainRuleViolationException(
                $"'{code}' is not a kudo category this department offers.");

    /// <summary>
    /// The label key for a code, falling back to a generated one.
    /// </summary>
    /// <remarks>
    /// A department can drop a category it once offered, and the kudos given under it remain — on somebody's
    /// annual review claim, most likely. Showing them under a generated key beats hiding them.
    /// </remarks>
    public string LabelFor(string code) =>
        _categories.TryGetValue(code, out var category) ? category.LabelKey : $"kudos.category.{code}";

    /// <summary>A price high enough for any department, low enough that one category cannot swamp a ladder.</summary>
    private const int MaximumCategoryPoints = 20;

    private static KudoMode ParseMode(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Contracts.KudoModes.PointsBadgesLeaderboard => KudoMode.PointsBadgesLeaderboard,
        Contracts.KudoModes.Points => KudoMode.Points,
        // Anything unrecognised is the default rather than an error, for the same reason malformed JSON is: the
        // quiet failure here is a department that counts, which is what it would have got by saying nothing.
        _ => KudoMode.Counter,
    };

    public static string ToCode(KudoMode mode) => mode switch
    {
        KudoMode.PointsBadgesLeaderboard => Contracts.KudoModes.PointsBadgesLeaderboard,
        KudoMode.Points => Contracts.KudoModes.Points,
        _ => Contracts.KudoModes.Counter,
    };

    private static ConfiguredRules? Parse(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? document.RootElement.Deserialize<ConfiguredRules>()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The stored shape.
    /// </summary>
    /// <remarks>
    /// The spec describes <c>category_points_json</c> and <c>badges_json</c> as separate blobs. They are one
    /// structure here: a price is a property of a category, and two documents keyed by the same codes drift the
    /// first time somebody adds a category to one and forgets the other.
    /// </remarks>
    private sealed record ConfiguredRules(
        [property: JsonPropertyName("mode")] string? Mode,
        [property: JsonPropertyName("monthlyCapPerGiver")] int? MonthlyCapPerGiver,
        [property: JsonPropertyName("categories")] List<ConfiguredCategory>? Categories,
        [property: JsonPropertyName("badges")] List<ConfiguredBadge>? Badges);

    private sealed record ConfiguredCategory(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("labelKey")] string? LabelKey,
        [property: JsonPropertyName("points")] int? Points);

    private sealed record ConfiguredBadge(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("labelKey")] string? LabelKey,
        [property: JsonPropertyName("minPoints")] int? MinPoints,
        [property: JsonPropertyName("minCount")] int? MinCount,
        [property: JsonPropertyName("category")] string? Category);
}
