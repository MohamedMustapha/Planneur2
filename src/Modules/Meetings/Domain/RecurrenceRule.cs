using System.Globalization;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Meetings.Domain;

/// <summary>How often a series repeats.</summary>
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
}

/// <summary>
/// An iCal RRULE, parsed, and the expansion of one into dates.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate subset of RFC 5545: FREQ (daily, weekly, monthly), INTERVAL, BYDAY — including the ordinal form
/// that expresses "the third Thursday" — BYMONTHDAY, COUNT and UNTIL. That covers every meeting an organization
/// actually runs. The parts left out (yearly rules, BYSETPOS, BYWEEKNO, EXDATE, timezone-bearing UNTIL) are the
/// parts that make a full implementation a project of its own, and none of them appear in a stand-up or a copil.
/// </para>
/// <para>
/// The point of standing on RRULE at all rather than inventing three columns is that the string survives contact
/// with the outside world: it is what an export to a real calendar would carry, and what an import would bring.
/// So an unsupported part is rejected loudly at write time rather than silently ignored, because a rule that
/// parses to something other than what it says is worse than one that is refused.
/// </para>
/// <para>
/// Pure, and therefore the one part of this module worth exhaustive unit tests. Nothing here knows about EF, the
/// database, or the request.
/// </para>
/// </remarks>
public sealed record RecurrenceRule
{
    /// <summary>
    /// The most candidates one expansion will consider.
    /// </summary>
    /// <remarks>
    /// A backstop, not a limit anybody should reach: a daily rule expanded over the longest horizon the
    /// materializer asks for produces a few hundred. It exists so a malformed INTERVAL cannot spin.
    /// </remarks>
    private const int MaximumCandidates = 10_000;

    private RecurrenceRule(
        RecurrenceFrequency frequency,
        int interval,
        IReadOnlyList<DayOfWeek> byDay,
        IReadOnlyList<OrdinalDay> byOrdinalDay,
        IReadOnlyList<int> byMonthDay,
        int? count,
        DateOnly? until)
    {
        Frequency = frequency;
        Interval = interval;
        ByDay = byDay;
        ByOrdinalDay = byOrdinalDay;
        ByMonthDay = byMonthDay;
        Count = count;
        Until = until;
    }

    public RecurrenceFrequency Frequency { get; }

    /// <summary>Every n periods. 1 unless stated.</summary>
    public int Interval { get; }

    /// <summary>Plain BYDAY: which weekdays, without an ordinal. Empty means "the anchor's own weekday".</summary>
    public IReadOnlyList<DayOfWeek> ByDay { get; }

    /// <summary>Ordinal BYDAY — <c>3TH</c>, <c>-1FR</c>. Monthly only.</summary>
    public IReadOnlyList<OrdinalDay> ByOrdinalDay { get; }

    /// <summary>BYMONTHDAY. Negative counts back from the end of the month, as RFC 5545 defines it.</summary>
    public IReadOnlyList<int> ByMonthDay { get; }

    /// <summary>Stop after this many occurrences, counted from the anchor — never from the window.</summary>
    public int? Count { get; }

    /// <summary>Stop on or before this date.</summary>
    public DateOnly? Until { get; }

    /// <summary>"the third Thursday" — <paramref name="Ordinal"/> is 1-based, or negative from the month's end.</summary>
    public readonly record struct OrdinalDay(int Ordinal, DayOfWeek Day);

    /// <summary>The weekly rule the UI offers by default, so a series can always be created without typing RRULE.</summary>
    public static RecurrenceRule Weekly(DayOfWeek day) =>
        new(RecurrenceFrequency.Weekly, 1, [day], [], [], null, null);

    /// <summary>Parses, or throws a domain rule violation the endpoint turns into a 422 with the reason.</summary>
    public static RecurrenceRule Parse(string text)
    {
        if (!TryParse(text, out var rule, out var error))
        {
            throw new DomainRuleViolationException(error);
        }

        return rule;
    }

    public static bool TryParse(string? text, out RecurrenceRule rule, out string error)
    {
        rule = Weekly(DayOfWeek.Monday);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "A recurrence rule is required.";

            return false;
        }

        // "RRULE:FREQ=WEEKLY;..." and "FREQ=WEEKLY;..." are both accepted: the prefix is part of the iCal line,
        // not of the rule, and whichever way a caller pasted it they meant the same thing.
        var body = text.Trim();

        if (body.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            body = body["RRULE:".Length..];
        }

        RecurrenceFrequency? frequency = null;
        var interval = 1;
        var byDay = new List<DayOfWeek>();
        var byOrdinalDay = new List<OrdinalDay>();
        var byMonthDay = new List<int>();
        int? count = null;
        DateOnly? until = null;

        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');

            if (separator <= 0)
            {
                error = $"'{part}' is not a NAME=VALUE pair.";

                return false;
            }

            var name = part[..separator].Trim().ToUpperInvariant();
            var value = part[(separator + 1)..].Trim();

            switch (name)
            {
                case "FREQ":
                    frequency = value.ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        _ => null,
                    };

                    if (frequency is null)
                    {
                        error = $"FREQ={value} is not supported. Use DAILY, WEEKLY or MONTHLY.";

                        return false;
                    }

                    break;

                case "INTERVAL":
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out interval) || interval < 1)
                    {
                        error = $"INTERVAL={value} must be a positive whole number.";

                        return false;
                    }

                    break;

                case "COUNT":
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out var parsedCount) || parsedCount < 1)
                    {
                        error = $"COUNT={value} must be a positive whole number.";

                        return false;
                    }

                    count = parsedCount;

                    break;

                case "UNTIL":
                    if (!TryParseUntil(value, out var parsedUntil))
                    {
                        error = $"UNTIL={value} must be a date in the form YYYYMMDD or YYYYMMDDTHHMMSSZ.";

                        return false;
                    }

                    until = parsedUntil;

                    break;

                case "BYDAY":
                    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!TryParseDay(token, out var day, out var ordinal))
                        {
                            error = $"BYDAY={token} is not a weekday. Use MO, TU, WE, TH, FR, SA, SU, optionally with an ordinal such as 3TH.";

                            return false;
                        }

                        if (ordinal is { } position)
                        {
                            byOrdinalDay.Add(new OrdinalDay(position, day));
                        }
                        else
                        {
                            byDay.Add(day);
                        }
                    }

                    break;

                case "BYMONTHDAY":
                    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!int.TryParse(token, CultureInfo.InvariantCulture, out var monthDay)
                            || monthDay == 0
                            || monthDay is < -31 or > 31)
                        {
                            error = $"BYMONTHDAY={token} must be between -31 and 31 and not zero.";

                            return false;
                        }

                        byMonthDay.Add(monthDay);
                    }

                    break;

                // WKST is accepted and ignored: this expansion anchors weeks on the series' own start date rather
                // than on a calendar week, so the week-start convention cannot change which dates come out.
                case "WKST":
                    break;

                default:
                    // Refused rather than skipped. Silently dropping BYSETPOS from a rule that carries it produces
                    // a schedule that is confidently wrong, which is the one outcome worth avoiding.
                    error = $"{name} is not supported in a recurrence rule.";

                    return false;
            }
        }

        if (frequency is not { } resolved)
        {
            error = "A recurrence rule needs a FREQ.";

            return false;
        }

        if (byOrdinalDay.Count > 0 && resolved is not RecurrenceFrequency.Monthly)
        {
            error = "An ordinal BYDAY such as 3TH only makes sense with FREQ=MONTHLY.";

            return false;
        }

        if (byMonthDay.Count > 0 && resolved is not RecurrenceFrequency.Monthly)
        {
            error = "BYMONTHDAY only makes sense with FREQ=MONTHLY.";

            return false;
        }

        if (count is not null && until is not null)
        {
            error = "A recurrence rule may carry COUNT or UNTIL, not both.";

            return false;
        }

        rule = new RecurrenceRule(resolved, interval, byDay, byOrdinalDay, byMonthDay, count, until);

        return true;
    }

    /// <summary>
    /// The dates this rule produces inside a window.
    /// </summary>
    /// <param name="anchor">The series' start date. The pattern is relative to it and COUNT is counted from it.</param>
    /// <param name="from">Window start, inclusive.</param>
    /// <param name="to">Window end, inclusive.</param>
    /// <remarks>
    /// Enumerates from the anchor rather than from the window, because COUNT and "every other week" are both
    /// defined relative to the start. Jumping straight to the window would make the tenth occurrence of a
    /// fortnightly series land on the wrong Monday half the time — and be right the other half, which is worse.
    /// </remarks>
    public IEnumerable<DateOnly> Expand(DateOnly anchor, DateOnly from, DateOnly to)
    {
        if (to < anchor || to < from)
        {
            yield break;
        }

        var horizon = Until is { } until && until < to ? until : to;
        var emitted = 0;
        var considered = 0;

        foreach (var candidate in Candidates(anchor, horizon))
        {
            if (++considered > MaximumCandidates)
            {
                yield break;
            }

            if (candidate < anchor)
            {
                continue;
            }

            if (candidate > horizon)
            {
                yield break;
            }

            emitted++;

            if (Count is { } limit && emitted > limit)
            {
                yield break;
            }

            if (candidate >= from)
            {
                yield return candidate;
            }
        }
    }

    /// <summary>The canonical RRULE string. Round-trips through <see cref="TryParse"/>.</summary>
    public override string ToString()
    {
        var parts = new List<string>
        {
            $"FREQ={Frequency.ToString().ToUpperInvariant()}",
        };

        if (Interval != 1)
        {
            parts.Add($"INTERVAL={Interval.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ByDay.Count > 0 || ByOrdinalDay.Count > 0)
        {
            var days = ByDay.Select(Code)
                .Concat(ByOrdinalDay.Select(entry => entry.Ordinal.ToString(CultureInfo.InvariantCulture) + Code(entry.Day)));

            parts.Add($"BYDAY={string.Join(',', days)}");
        }

        if (ByMonthDay.Count > 0)
        {
            parts.Add($"BYMONTHDAY={string.Join(',', ByMonthDay.Select(day => day.ToString(CultureInfo.InvariantCulture)))}");
        }

        if (Count is { } count)
        {
            parts.Add($"COUNT={count.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Until is { } until)
        {
            parts.Add($"UNTIL={until:yyyyMMdd}");
        }

        return string.Join(';', parts);
    }

    /// <summary>Every date the pattern produces from the anchor onward, ascending, before COUNT and the window apply.</summary>
    private IEnumerable<DateOnly> Candidates(DateOnly anchor, DateOnly horizon) => Frequency switch
    {
        RecurrenceFrequency.Daily => Daily(anchor, horizon),
        RecurrenceFrequency.Weekly => WeeklyCandidates(anchor, horizon),
        _ => MonthlyCandidates(anchor, horizon),
    };

    private IEnumerable<DateOnly> Daily(DateOnly anchor, DateOnly horizon)
    {
        for (var day = anchor; day <= horizon; day = day.AddDays(Interval))
        {
            yield return day;
        }
    }

    private IEnumerable<DateOnly> WeeklyCandidates(DateOnly anchor, DateOnly horizon)
    {
        // Weeks are counted from the anchor's own week, not from a calendar week: "every other Tuesday starting
        // the 3rd" means the 3rd and the 17th whatever the ISO week numbers happen to be.
        var days = ByDay.Count > 0 ? ByDay.Distinct().ToArray() : [anchor.DayOfWeek];
        var weekStart = anchor.AddDays(-DaysSinceMonday(anchor.DayOfWeek));

        for (var week = weekStart; week <= horizon; week = week.AddDays(7 * Interval))
        {
            // Ordered within the week so the overall sequence stays ascending even when BYDAY is written out of
            // order, which it usually is: MO,WE,FR is how people type it, and TH,MO is how they sometimes do.
            foreach (var day in days.Select(DaysSinceMonday).Order())
            {
                yield return week.AddDays(day);
            }
        }
    }

    private IEnumerable<DateOnly> MonthlyCandidates(DateOnly anchor, DateOnly horizon)
    {
        var month = new DateOnly(anchor.Year, anchor.Month, 1);

        while (month <= horizon)
        {
            foreach (var day in DaysInMonth(month, anchor.Day).Order())
            {
                yield return day;
            }

            month = month.AddMonths(Interval);
        }
    }

    private IEnumerable<DateOnly> DaysInMonth(DateOnly month, int anchorDay)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);

        if (ByMonthDay.Count > 0)
        {
            foreach (var day in ByMonthDay.Distinct())
            {
                // RFC 5545: a negative BYMONTHDAY counts back from the end, so -1 is the last day whether the
                // month has 28 or 31. A positive day past the end simply does not occur that month — February
                // has no 30th, and inventing one would move the meeting.
                var resolved = day > 0 ? day : length + day + 1;

                if (resolved >= 1 && resolved <= length)
                {
                    yield return new DateOnly(month.Year, month.Month, resolved);
                }
            }

            yield break;
        }

        if (ByOrdinalDay.Count > 0)
        {
            foreach (var entry in ByOrdinalDay.Distinct())
            {
                if (NthWeekday(month, entry) is { } day)
                {
                    yield return day;
                }
            }

            yield break;
        }

        // Neither given: the anchor's own day of the month, which is the reading of a bare FREQ=MONTHLY. A month
        // too short for it is skipped rather than clamped, for the same reason as above — a series anchored on
        // the 31st happens seven times a year, and moving it to the 28th would invent a meeting nobody scheduled.
        if (anchorDay <= length)
        {
            yield return new DateOnly(month.Year, month.Month, anchorDay);
        }
    }

    private static DateOnly? NthWeekday(DateOnly month, OrdinalDay entry)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);

        if (entry.Ordinal > 0)
        {
            var first = new DateOnly(month.Year, month.Month, 1);
            var offset = ((int)entry.Day - (int)first.DayOfWeek + 7) % 7;
            var day = 1 + offset + (entry.Ordinal - 1) * 7;

            return day <= length ? new DateOnly(month.Year, month.Month, day) : null;
        }

        var last = new DateOnly(month.Year, month.Month, length);
        var back = ((int)last.DayOfWeek - (int)entry.Day + 7) % 7;
        var fromEnd = length - back + (entry.Ordinal + 1) * 7;

        return fromEnd >= 1 ? new DateOnly(month.Year, month.Month, fromEnd) : null;
    }

    /// <summary>Monday-based day index. The org runs on ISO weeks and every board starts on Monday.</summary>
    private static int DaysSinceMonday(DayOfWeek day) => ((int)day + 6) % 7;

    private static bool TryParseUntil(string value, out DateOnly until)
    {
        until = default;

        // The full form carries a time and a Z. Truncating to the date is right for this module: expansion works
        // in whole days, and an UNTIL an hour into a day either includes that day's occurrence or does not
        // depending on a time the series may not even have had when the rule was written.
        var date = value.Length >= 8 ? value[..8] : value;

        return DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out until);
    }

    private static bool TryParseDay(string token, out DayOfWeek day, out int? ordinal)
    {
        day = DayOfWeek.Monday;
        ordinal = null;

        var text = token.Trim().ToUpperInvariant();

        if (text.Length < 2)
        {
            return false;
        }

        var code = text[^2..];
        var prefix = text[..^2];

        if (prefix.Length > 0)
        {
            if (!int.TryParse(prefix, CultureInfo.InvariantCulture, out var position) || position == 0 || position is < -5 or > 5)
            {
                return false;
            }

            ordinal = position;
        }

        day = code switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => (DayOfWeek)(-1),
        };

        return day is not (DayOfWeek)(-1);
    }

    private static string Code(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };
}
