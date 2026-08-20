using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Scheduling.Domain;

namespace Cracra.Tests.Unit.Scheduling;

/// <summary>
/// The 6b shift scheduler's rules: template resolution, double-booking, and coverage.
/// </summary>
/// <remarks>
/// The distinction worth pinning here is which failures refuse and which merely warn. A double-booking is a
/// data-entry mistake and is refused; a thin Tuesday afternoon is a staffing judgement and is only reported.
/// </remarks>
public sealed class ShiftTests
{
    private static readonly Guid Person = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Colleague = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 8, 17);

    private static ShiftTemplate Morning => ShiftTemplate.Defaults.Single(t => t.Code == "morning");

    private static ShiftTemplate Afternoon => ShiftTemplate.Defaults.Single(t => t.Code == "afternoon");

    [Fact]
    public void A_department_that_configured_nothing_gets_the_default_slots()
    {
        var templates = ShiftTemplate.Resolve(null);

        // A shift board rendering no slots at all would look broken rather than unconfigured.
        templates.Select(template => template.Code).ShouldBe(["morning", "afternoon", "on-call"]);
    }

    [Fact]
    public void A_department_replaces_the_slots_wholesale()
    {
        var templates = ShiftTemplate.Resolve("""
            {"shifts":[
              {"code":"nuit","labelKey":"shift.nuit","start":"22:00","end":"06:00","minimumStaff":2}
            ]}
            """);

        // Replaced, not merged — unlike the activity taxonomy. Nothing downstream reports on shift codes, so a
        // department defining its own is not dropping vocabulary anyone else depends on.
        templates.ShouldHaveSingleItem().Code.ShouldBe("nuit");
    }

    [Fact]
    public void A_slot_running_past_midnight_ends_the_next_day()
    {
        var night = ShiftTemplate.Resolve("""
            {"shifts":[{"code":"nuit","start":"22:00","end":"06:00","minimumStaff":1}]}
            """).Single();

        var (start, end) = night.SpanOn(Monday);

        end.ShouldBeGreaterThan(start);
        end.Date.ShouldBe(Monday.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void Malformed_configuration_falls_back_to_the_defaults()
    {
        // One bad edit in a settings screen must not take a unit's roster offline.
        foreach (var broken in new[] { "not json", "[]", """{"shifts":"nope"}""", """{"shifts":[{"code":"x","start":"nope","end":"nope"}]}""" })
        {
            ShiftTemplate.Resolve(broken).ShouldBe(ShiftTemplate.Defaults);
        }
    }

    [Fact]
    public void A_shift_takes_its_span_from_its_template()
    {
        var shift = Plan(Morning, Monday, []);

        shift.Hours.ShouldBe(4.5m);
        shift.Day.ShouldBe(Monday);
        shift.TemplateCode.ShouldBe("morning");
    }

    [Fact]
    public void The_same_person_cannot_be_booked_twice_over()
    {
        var morning = Plan(Morning, Monday, []);

        // Two shifts at once is not a staffing risk to weigh up — it is a mistake, and the coverage numbers it
        // would produce are simply wrong.
        Should.Throw<DomainRuleViolationException>(() => Plan(Morning, Monday, [morning]));
    }

    [Fact]
    public void Two_non_overlapping_shifts_on_one_day_are_fine()
    {
        var morning = Plan(Morning, Monday, []);

        Should.NotThrow(() => Plan(Afternoon, Monday, [morning]));
    }

    [Fact]
    public void Moving_a_shift_ignores_its_own_previous_position()
    {
        var shift = Plan(Morning, Monday, []);

        // Otherwise a shift would always collide with itself and could never be moved at all.
        Should.NotThrow(() => shift.MoveTo(Afternoon, Monday, [shift], Person, Now));

        shift.TemplateCode.ShouldBe("afternoon");
    }

    [Fact]
    public void Coverage_reports_a_slot_that_is_short()
    {
        var templates = new[] { Morning with { MinimumStaff = 2 } };

        var gaps = ShiftCoverage.Check(templates, [Plan(Morning, Monday, [])], Monday, Monday);

        var gap = gaps.ShouldHaveSingleItem();

        gap.Required.ShouldBe(2);
        gap.Scheduled.ShouldBe(1);
        gap.Day.ShouldBe(Monday);
    }

    [Fact]
    public void Coverage_is_satisfied_once_the_minimum_is_met()
    {
        var templates = new[] { Morning with { MinimumStaff = 2 } };

        var shifts = new[] { Plan(Morning, Monday, []), Plan(Morning, Monday, [], Colleague) };

        ShiftCoverage.Check(templates, shifts, Monday, Monday).ShouldBeEmpty();
    }

    [Fact]
    public void A_slot_with_no_minimum_is_never_reported()
    {
        // On-call has no minimum by default: it is a rota, not a staffing floor, and flagging it every day would
        // train people to ignore the warnings that matter.
        ShiftCoverage.Check(ShiftTemplate.Defaults, [], Monday, Monday)
            .Select(gap => gap.TemplateCode)
            .ShouldNotContain("on-call");
    }

    [Fact]
    public void Weekends_are_not_policed()
    {
        var templates = new[] { Morning with { MinimumStaff = 1 } };

        var gaps = ShiftCoverage.Check(templates, [], Monday, Monday.AddDays(6));

        // Five working days, not seven. A Saturday nobody is rostered on is the office being shut.
        gaps.Count.ShouldBe(5);
        gaps.ShouldAllBe(gap => gap.Day.DayOfWeek != DayOfWeek.Saturday && gap.Day.DayOfWeek != DayOfWeek.Sunday);
    }

    private static Shift Plan(
        ShiftTemplate template,
        DateOnly day,
        IReadOnlyList<Shift> existing,
        Guid? personId = null) =>
        Shift.Plan(personId ?? Person, Unit, Department, template, day, existing, Person, Now);
}
