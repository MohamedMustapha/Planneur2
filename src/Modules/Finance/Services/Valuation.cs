using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Finance.Services;

/// <summary>
/// What an hour is worth, resolved once for a whole view.
/// </summary>
/// <remarks>
/// <para>
/// A rate card is keyed by functional role id, and an activity entry knows a person. Bridging the two needs the
/// directory twice — the role ids' codes, and each person's roles — and doing that per entry would be two round
/// trips per logged hour. So it is done once, up front, over the distinct departments the entries touch.
/// </para>
/// <para>
/// Every lookup that fails leaves the hour unvalued rather than valued at zero. The distinction carries all the
/// way to the screen: a bucket with hours and no money says "we have not priced this", which is actionable,
/// where a bucket with hours and a zero says "this was free", which is false.
/// </para>
/// </remarks>
internal sealed class Valuation
{
    private readonly Dictionary<Guid, IReadOnlyList<string>> _rolesByPerson = [];
    private readonly List<(string RoleCode, RateCardView Card)> _cards = [];

    private Valuation()
    {
    }

    /// <summary>An empty valuation: nothing priced, every hour reported in hours only.</summary>
    public static Valuation None { get; } = new();

    public static async Task<Valuation> BuildAsync(
        IReadOnlyList<RateCardView> rates,
        IReadOnlyList<ActivityEntryView> entries,
        IDirectoryReader directory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(entries);

        if (rates.Count == 0 || entries.Count == 0)
        {
            return None;
        }

        var valuation = new Valuation();

        // Role ids to codes, because a person's summary carries codes and a rate card carries an id. Resolving in
        // this direction rather than the other keeps the rate card's key the one the spec names and the one a
        // future rate-card editor will offer.
        var roleCodes = await directory.GetFunctionalRoleCodesAsync(
            [.. rates.Select(rate => rate.FunctionalRoleId).Distinct()], ct);

        foreach (var rate in rates)
        {
            if (roleCodes.TryGetValue(rate.FunctionalRoleId, out var code))
            {
                valuation._cards.Add((code, rate));
            }
        }

        if (valuation._cards.Count == 0)
        {
            return None;
        }

        foreach (var departmentId in entries.Select(entry => entry.DepartmentId).Distinct())
        {
            foreach (var person in await directory.GetPeopleAsync(null, departmentId, ct))
            {
                // Whatever RLS handed over. Somebody the caller may not see keeps their hours in the view — the
                // entries came back, so the matrix already allowed the work to be counted — and simply goes
                // unvalued, which is visible rather than silent.
                valuation._rolesByPerson[person.Id] = person.FunctionalRoleCodes;
            }
        }

        return valuation;
    }

    /// <summary>The money one entry represents, or null where nothing prices it.</summary>
    public decimal? Value(ActivityEntryView entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_cards.Count == 0 || !_rolesByPerson.TryGetValue(entry.PersonId, out var roles) || roles.Count == 0)
        {
            return null;
        }

        var day = DateOnly.FromDateTime(entry.SlotStart.UtcDateTime);

        foreach (var role in roles)
        {
            var card = _cards.FirstOrDefault(candidate =>
                string.Equals(candidate.RoleCode, role, StringComparison.OrdinalIgnoreCase)
                && candidate.Card.CoversDay(day));

            if (card.Card is not null)
            {
                return entry.Hours * card.Card.HourlyRate;
            }
        }

        // A person whose roles are all unpriced. Their hours count; their cost does not, and the view says so.
        return null;
    }
}
