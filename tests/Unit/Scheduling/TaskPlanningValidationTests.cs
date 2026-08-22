using Cracra.Modules.Scheduling.Application;

namespace Cracra.Tests.Unit.Scheduling;

/// <summary>
/// The two 6c write commands, at the door.
/// </summary>
/// <remarks>
/// Both handlers go straight through to Activities, which owns every domain rule they could restate. What is left
/// here is the shape of the request itself — and that is worth pinning down precisely because it is the only check
/// standing between a mis-drawn rectangle and a row in somebody's week: a zero-length block, a percentage of 400,
/// a task drawn on nobody.
/// </remarks>
public sealed class TaskPlanningValidationTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    private static readonly PlanTaskValidator PlanValidator = new();

    private static readonly SetTaskProgressValidator ProgressValidator = new();

    [Fact]
    public void A_task_drawn_on_a_row_over_a_span_is_accepted()
    {
        PlanValidator.Validate(Plan()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_task_needs_somebody_to_belong_to()
    {
        // The board never sends this, but the endpoint is Authenticated rather than lead-only: an empty id would
        // otherwise reach Activities as a placement lookup for Guid.Empty and come back as a 404 nobody can read.
        PlanValidator.Validate(Plan() with { PersonId = Guid.Empty }).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_task_needs_an_activity_type(string code)
    {
        PlanValidator.Validate(Plan() with { ActivityTypeCode = code }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_activity_type_longer_than_the_column_is_refused()
    {
        PlanValidator.Validate(Plan() with { ActivityTypeCode = new string('x', 65) }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_note_longer_than_the_column_is_refused()
    {
        PlanValidator.Validate(Plan() with { Note = new string('x', 2001) }).IsValid.ShouldBeFalse();
        PlanValidator.Validate(Plan() with { Note = new string('x', 2000) }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_block_must_end_after_it_starts()
    {
        // A drag that snapped back on itself. Left to Activities this becomes a slot rule violation; caught here it
        // is a 400 on a request that was never coherent.
        PlanValidator.Validate(Plan() with { End = MondayMorning }).IsValid.ShouldBeFalse();
        PlanValidator.Validate(Plan() with { End = MondayMorning.AddMinutes(-30) }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_task_may_be_drawn_without_a_project_or_a_note_or_a_percentage()
    {
        // All three are the popup's optional half. Whether this type in fact demands a project is the department's
        // taxonomy talking, and this validator deliberately does not have an opinion about it.
        var bare = Plan() with { ProjectId = null, Note = null, PercentComplete = null };

        PlanValidator.Validate(bare).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Progress_within_the_scale_is_accepted(int percent)
    {
        PlanValidator.Validate(Plan() with { PercentComplete = percent }).IsValid.ShouldBeTrue();
        ProgressValidator.Validate(Progress(percent)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void Progress_off_the_scale_is_refused(int percent)
    {
        PlanValidator.Validate(Plan() with { PercentComplete = percent }).IsValid.ShouldBeFalse();
        ProgressValidator.Validate(Progress(percent)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Clearing_progress_is_not_a_missing_value()
    {
        // Null is the gesture that hands the figure back to plan-versus-actual, so the range rule has to stand
        // aside for it rather than treat it as an absent field.
        ProgressValidator.Validate(Progress(null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Progress_needs_the_entry_it_belongs_to()
    {
        ProgressValidator.Validate(new SetTaskProgressCommand(Guid.Empty, 50)).IsValid.ShouldBeFalse();
    }

    private static PlanTaskCommand Plan() => new(
        PersonId: Guid.CreateVersion7(),
        ActivityTypeCode: "project-build",
        ProjectId: Guid.CreateVersion7(),
        Start: MondayMorning,
        End: MondayMorning.AddHours(3),
        Note: "Reprise du socle",
        PercentComplete: 25);

    private static SetTaskProgressCommand Progress(int? percent) => new(Guid.CreateVersion7(), percent);
}
