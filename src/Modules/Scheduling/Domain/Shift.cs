using System.Text.Json;
using System.Text.Json.Serialization;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Scheduling.Domain;

/// <summary>
/// One person's shift on one day.
/// </summary>
/// <remarks>
/// The 6b archetype's unit. Deliberately a day plus a template rather than a free time range: a shift scheduler
/// exists precisely because the slots are standard, and letting anyone draw an arbitrary range would turn the
/// coverage check into a question nobody can answer.
/// </remarks>
public sealed class Shift
{
    private readonly List<object> _domainEvents = [];

    private Shift()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    public Guid PersonId { get; private init; }

    public Guid UnitId { get; private init; }

    public Guid DepartmentId { get; private init; }

    public string TemplateCode { get; private set; } = string.Empty;

    public DateOnly Day { get; private set; }

    public DateTimeOffset Start { get; private set; }

    public DateTimeOffset End { get; private set; }

    public decimal Hours { get; private set; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid ModifiedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public static Shift Plan(
        Guid personId,
        Guid unitId,
        Guid departmentId,
        ShiftTemplate template,
        DateOnly day,
        IReadOnlyList<Shift> existingForPerson,
        Guid plannedBy,
        DateTimeOffset now)
    {
        if (personId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A shift needs someone to work it.");
        }

        var (start, end) = template.SpanOn(day);

        // Double-booking is refused rather than warned about. Two shifts at once is not a staffing risk somebody
        // should weigh up — it is a data-entry mistake, and the coverage numbers it produces are simply wrong.
        foreach (var existing in existingForPerson)
        {
            if (existing.Day == day && existing.Start < end && start < existing.End)
            {
                throw new DomainRuleViolationException(
                    $"That person already has a shift overlapping {template.LabelKey} on {day:yyyy-MM-dd}.");
            }
        }

        var shift = new Shift
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            UnitId = unitId,
            DepartmentId = departmentId,
            TemplateCode = template.Code,
            Day = day,
            Start = start,
            End = end,
            Hours = template.Hours,
            CreatedBy = plannedBy,
            ModifiedBy = plannedBy,
            CreatedAt = now,
            ModifiedAt = now,
        };

        shift._domainEvents.Add(new ShiftPlanned(shift.Id, personId, day, template.Code));

        return shift;
    }

    public void MoveTo(
        ShiftTemplate template,
        DateOnly day,
        IReadOnlyList<Shift> existingForPerson,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        var (start, end) = template.SpanOn(day);

        foreach (var existing in existingForPerson)
        {
            if (existing.Id != Id && existing.Day == day && existing.Start < end && start < existing.End)
            {
                throw new DomainRuleViolationException(
                    $"That person already has a shift overlapping {template.LabelKey} on {day:yyyy-MM-dd}.");
            }
        }

        TemplateCode = template.Code;
        Day = day;
        Start = start;
        End = end;
        Hours = template.Hours;

        ModifiedBy = modifiedBy;
        ModifiedAt = now;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public sealed record ShiftPlanned(Guid ShiftId, Guid PersonId, DateOnly Day, string TemplateCode);

/// <summary>
/// A shift slot a department offers: morning, afternoon, on-call, whatever they call theirs.
/// </summary>
/// <param name="MinimumStaff">How many people this slot needs. Zero means the slot exists but is not policed.</param>
public sealed record ShiftTemplate(
    string Code,
    string LabelKey,
    TimeOnly Start,
    TimeOnly End,
    int MinimumStaff,
    string? Color)
{
    /// <summary>
    /// The platform defaults, for a department that has configured nothing.
    /// </summary>
    /// <remarks>
    /// A shift board that renders no slots at all until somebody edits a JSON blob would look broken rather than
    /// unconfigured. These three cover the common case — two day slots and an on-call band — and a department
    /// replaces them wholesale, since unlike the activity taxonomy nothing downstream depends on these codes.
    /// </remarks>
    public static readonly IReadOnlyList<ShiftTemplate> Defaults =
    [
        new("morning", "shift.morning", new TimeOnly(8, 0), new TimeOnly(12, 30), 1, "var(--activity-run)"),
        new("afternoon", "shift.afternoon", new TimeOnly(13, 30), new TimeOnly(18, 0), 1, "var(--activity-build)"),
        new("on-call", "shift.on-call", new TimeOnly(18, 0), new TimeOnly(22, 0), 0, "var(--activity-leave)"),
    ];

    public decimal Hours => Math.Round((decimal)(End - Start).TotalHours, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The instants this slot occupies on a given day.
    /// </summary>
    /// <remarks>
    /// UTC, like every other instant the platform stores. A department working across time zones would need more
    /// than this, and nothing in the brief suggests one does — inventing zone handling now would be guessing at a
    /// requirement rather than meeting one.
    /// </remarks>
    public (DateTimeOffset Start, DateTimeOffset End) SpanOn(DateOnly day)
    {
        var start = new DateTimeOffset(day.ToDateTime(Start), TimeSpan.Zero);
        var end = new DateTimeOffset(day.ToDateTime(End), TimeSpan.Zero);

        // An on-call slot ending at midnight or beyond runs into the next day rather than backwards.
        return end <= start ? (start, end.AddDays(1)) : (start, end);
    }

    /// <summary>
    /// Reads a department's configured templates, falling back to the defaults.
    /// </summary>
    /// <remarks>
    /// Malformed configuration falls back rather than throwing, for the same reason the activity taxonomy does:
    /// one bad edit in a settings screen must not take a unit's shift board offline.
    /// </remarks>
    public static IReadOnlyList<ShiftTemplate> Resolve(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return Defaults;
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);

            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty("shifts", out var shifts)
                || shifts.ValueKind is not JsonValueKind.Array)
            {
                return Defaults;
            }

            var configured = shifts.Deserialize<List<ConfiguredShift>>() ?? [];

            var parsed = configured
                .Where(shift => !string.IsNullOrWhiteSpace(shift.Code))
                .Select(Parse)
                .OfType<ShiftTemplate>()
                .ToList();

            return parsed.Count > 0 ? parsed : Defaults;
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    private static ShiftTemplate? Parse(ConfiguredShift shift)
    {
        if (!TimeOnly.TryParse(shift.Start, out var start) || !TimeOnly.TryParse(shift.End, out var end))
        {
            return null;
        }

        var code = shift.Code.Trim().ToLowerInvariant();

        return new ShiftTemplate(
            code,
            string.IsNullOrWhiteSpace(shift.LabelKey) ? $"shift.{code}" : shift.LabelKey.Trim(),
            start,
            end,
            Math.Max(0, shift.MinimumStaff ?? 0),
            shift.Color);
    }

    private sealed record ConfiguredShift(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("labelKey")] string? LabelKey,
        [property: JsonPropertyName("start")] string? Start,
        [property: JsonPropertyName("end")] string? End,
        [property: JsonPropertyName("minimumStaff")] int? MinimumStaff,
        [property: JsonPropertyName("color")] string? Color);
}

/// <summary>
/// Checks a week's shifts against each slot's minimum staffing.
/// </summary>
/// <remarks>
/// Warnings, never refusals. A Tuesday afternoon with one person instead of two is a fact a lead needs to see and
/// then decide about — sometimes that is genuinely fine, and a scheduler that refused to record it would just be
/// a scheduler people stopped using.
/// </remarks>
public static class ShiftCoverage
{
    public static IReadOnlyList<CoverageGap> Check(
        IReadOnlyList<ShiftTemplate> templates,
        IReadOnlyList<Shift> shifts,
        DateOnly from,
        DateOnly to)
    {
        var gaps = new List<CoverageGap>();

        foreach (var template in templates.Where(candidate => candidate.MinimumStaff > 0))
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                // Weekends are not policed. A slot that nobody is rostered on because the office is shut is not a
                // coverage gap, and flagging every Saturday would train people to ignore the warnings.
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var scheduled = shifts.Count(shift =>
                    shift.Day == day && string.Equals(shift.TemplateCode, template.Code, StringComparison.OrdinalIgnoreCase));

                if (scheduled < template.MinimumStaff)
                {
                    gaps.Add(new CoverageGap(day, template.Code, template.MinimumStaff, scheduled));
                }
            }
        }

        return gaps;
    }
}

public sealed record CoverageGap(DateOnly Day, string TemplateCode, int Required, int Scheduled);
