using Cracra.Modules.Activities.Domain;

namespace Cracra.Tests.Unit.Activities;

/// <summary>
/// Resolving a department's working day.
/// </summary>
/// <remarks>
/// The rule this file exists to pin down is the fallback one. A board that refuses to draw because a settings blob
/// is malformed is a worse failure than a board drawn on the platform's hours, so every unreadable shape has to
/// come back as the default rather than as an exception — and the boundary between "refine this" and "ignore
/// this" is exactly where a subtle bug would hide.
/// </remarks>
public sealed class WorkingDayPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Falls_back_to_the_platform_day_when_nothing_is_configured(string? configured)
    {
        WorkingDayPolicy.Resolve(configured).ShouldBe(WorkingDayPolicy.Default);
    }

    [Fact]
    public void Reads_a_department_that_configured_its_own_hours()
    {
        var day = WorkingDayPolicy.Resolve(
            """
            {
              "dayStart": "07:00",
              "dayEnd": "16:00",
              "morning": { "start": "07:30", "end": "12:00" },
              "afternoon": { "start": "12:30", "end": "15:30" }
            }
            """);

        day.DayStart.ShouldBe(new TimeOnly(7, 0));
        day.DayEnd.ShouldBe(new TimeOnly(16, 0));
        day.MorningStart.ShouldBe(new TimeOnly(7, 30));
        day.AfternoonEnd.ShouldBe(new TimeOnly(15, 30));
    }

    [Fact]
    public void Fills_the_gaps_of_a_partial_configuration_from_the_default()
    {
        // A department that only wants a later start should not have to restate the other five values, and
        // should certainly not lose them.
        var day = WorkingDayPolicy.Resolve("""{"dayStart":"07:00"}""");

        day.DayStart.ShouldBe(new TimeOnly(7, 0));
        day.DayEnd.ShouldBe(WorkingDayPolicy.Default.DayEnd);
        day.MorningStart.ShouldBe(WorkingDayPolicy.Default.MorningStart);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"dayStart":"25:99"}""")]
    public void Falls_back_rather_than_throwing_on_anything_unreadable(string configured)
    {
        Should.NotThrow(() => WorkingDayPolicy.Resolve(configured));
        WorkingDayPolicy.Resolve(configured).ShouldBe(WorkingDayPolicy.Default);
    }

    [Fact]
    public void Refuses_a_day_that_contradicts_itself()
    {
        // Every value parses; together they describe an afternoon outside the day it belongs to. Taking this at
        // face value would draw an axis the quick-add's own preset falls off the end of.
        var day = WorkingDayPolicy.Resolve(
            """
            {
              "dayStart": "09:00",
              "dayEnd": "17:00",
              "morning": { "start": "09:00", "end": "13:00" },
              "afternoon": { "start": "14:00", "end": "19:00" }
            }
            """);

        day.ShouldBe(WorkingDayPolicy.Default);
    }

    [Fact]
    public void Accepts_a_day_with_no_lunch_break()
    {
        // Unusual, not invalid — a shift-working unit may run its two halves back to back.
        var day = WorkingDayPolicy.Resolve(
            """
            {
              "dayStart": "06:00",
              "dayEnd": "20:00",
              "morning": { "start": "08:00", "end": "12:00" },
              "afternoon": { "start": "12:00", "end": "16:00" }
            }
            """);

        day.MorningEnd.ShouldBe(new TimeOnly(12, 0));
        day.AfternoonStart.ShouldBe(new TimeOnly(12, 0));
    }
}
