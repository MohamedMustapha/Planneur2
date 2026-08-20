namespace Cracra.Modules.Kudos.Domain;

/// <summary>One kudo, reduced to what a ladder cares about.</summary>
public sealed record KudoRecord(string Category, int Points, DateTimeOffset CreatedAt);

/// <summary>A badge somebody has earned, and the moment they earned it.</summary>
public sealed record EarnedBadge(string Code, string LabelKey, DateTimeOffset EarnedAt);

/// <summary>
/// What a person has accumulated, as the badge criteria read it.
/// </summary>
/// <remarks>
/// Counts and points, in total and per category, because a badge may be keyed to either — "five mentoring kudos"
/// is a different claim from "twenty-five points", and a ladder that could only express the second would flatten
/// the kind of recognition a review actually asks about.
/// </remarks>
public sealed record KudoTally(
    int Count,
    int Points,
    IReadOnlyDictionary<string, int> CountByCategory,
    IReadOnlyDictionary<string, int> PointsByCategory)
{
    public static KudoTally Empty { get; } = new(
        0,
        0,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    public KudoTally Plus(string category, int points)
    {
        var counts = new Dictionary<string, int>(CountByCategory, StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<string, int>(PointsByCategory, StringComparer.OrdinalIgnoreCase);

        counts[category] = counts.GetValueOrDefault(category) + 1;
        scores[category] = scores.GetValueOrDefault(category) + points;

        return new KudoTally(Count + 1, Points + points, counts, scores);
    }

    public int CountIn(string? category) =>
        category is null ? Count : CountByCategory.GetValueOrDefault(category);

    public int PointsIn(string? category) =>
        category is null ? Points : PointsByCategory.GetValueOrDefault(category);
}

/// <summary>
/// Badges, derived from the kudos on record rather than stored as awards.
/// </summary>
/// <remarks>
/// <para>
/// The spec models a <c>PersonBadge</c> row written when a threshold is met. This does not, and the reason is that
/// every input to the answer is still present: the kudos are rows, the thresholds are department configuration,
/// and a stored award is a cached answer to a question that can always be asked again. Caches of that kind go
/// wrong in one specific way here — a department raises Cornerstone from twenty-five points to fifty and half the
/// unit keeps a badge nobody can any longer explain, standing next to colleagues with more points and no badge.
/// </para>
/// <para>
/// Deriving also removes a row one person's session would have had to write about another person. A badge belongs
/// to the receiver; the act that triggers it is the giver's. Storing it would mean either an insert policy wide
/// enough to be no policy at all, or a second transaction that can commit when the kudo it came from does not.
/// </para>
/// <para>
/// What is <em>not</em> given up is the moment: replaying somebody's kudos in order recovers exactly which one
/// took them over each bar, so <see cref="EarnedBadge.EarnedAt"/> is a real date and
/// <see cref="Contracts.BadgeAwarded"/> can still be raised at the instant it becomes true.
/// </para>
/// </remarks>
public static class BadgeLadder
{
    /// <summary>True when a tally meets every condition the badge sets.</summary>
    public static bool IsEarned(BadgeDefinition badge, KudoTally tally)
    {
        if (!badge.Criteria.IsMeaningful)
        {
            // A badge with no bar would be earned by everybody on their first kudo. Refused at parse time too;
            // repeated here because this is the method every caller actually goes through.
            return false;
        }

        if (badge.Criteria.MinCount is { } minCount && tally.CountIn(badge.Criteria.Category) < minCount)
        {
            return false;
        }

        return badge.Criteria.MinPoints is not { } minPoints
               || tally.PointsIn(badge.Criteria.Category) >= minPoints;
    }

    /// <summary>
    /// The badges crossed by going from one tally to the next.
    /// </summary>
    /// <remarks>
    /// Used at write time, to say what a single kudo just earned somebody. Only badges that were <em>not</em>
    /// earned before count, so a person who is already Cornerstone does not get told so again on every kudo.
    /// </remarks>
    public static IReadOnlyList<BadgeDefinition> NewlyEarned(KudoRules rules, KudoTally before, KudoTally after) =>
        !rules.ShowsPoints
            ? []
            :
            [
                .. rules.Badges.Where(badge => IsEarned(badge, after) && !IsEarned(badge, before)),
            ];

    /// <summary>
    /// Every badge somebody holds, with the date they earned it.
    /// </summary>
    /// <remarks>
    /// Replays the kudos in chronological order and stamps each badge with the kudo that crossed its bar. Ordering
    /// is done here rather than assumed of the caller: a query that returned newest-first — which most of them do,
    /// because that is what a wall wants — would otherwise date every badge to somebody's first kudo.
    /// </remarks>
    public static IReadOnlyList<EarnedBadge> Earned(KudoRules rules, IEnumerable<KudoRecord> kudos)
    {
        if (!rules.ShowsPoints)
        {
            // Badges are the points half of the ladder. A department counting recognitions has said it does not
            // want a score, and a badge is a score with a name on it.
            return [];
        }

        var earned = new List<EarnedBadge>();
        var outstanding = rules.Badges.Where(badge => badge.Criteria.IsMeaningful).ToList();
        var tally = KudoTally.Empty;

        foreach (var kudo in kudos.OrderBy(kudo => kudo.CreatedAt))
        {
            tally = tally.Plus(kudo.Category, kudo.Points);

            // Iterated backwards so the earned ones can be removed as we go without disturbing the walk.
            for (var index = outstanding.Count - 1; index >= 0; index--)
            {
                if (!IsEarned(outstanding[index], tally))
                {
                    continue;
                }

                earned.Add(new EarnedBadge(outstanding[index].Code, outstanding[index].LabelKey, kudo.CreatedAt));
                outstanding.RemoveAt(index);
            }
        }

        return [.. earned.OrderBy(badge => badge.EarnedAt)];
    }
}
