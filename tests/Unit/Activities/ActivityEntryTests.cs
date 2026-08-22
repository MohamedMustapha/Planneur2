using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Activities.Domain;

namespace Cracra.Tests.Unit.Activities;

/// <summary>
/// The entry aggregate: taxonomy enforcement, slot arithmetic, and plan-to-actual reconciliation.
/// </summary>
public sealed class ActivityEntryTests
{
    private static readonly Guid Person = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Colleague = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Project = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ActivityTaxonomy Taxonomy = ActivityTaxonomy.Resolve(null);

    [Fact]
    public void An_entry_takes_its_hours_from_its_slot_by_default()
    {
        var entry = Log(start: Hour(9), end: Hour(12));

        entry.Hours.ShouldBe(3m);
    }

    [Fact]
    public void Hours_may_be_less_than_the_slot()
    {
        // A two-hour meeting in which thirty minutes were this activity is a real thing to record.
        var entry = Log(start: Hour(9), end: Hour(11), hours: 0.5m);

        entry.Hours.ShouldBe(0.5m);
    }

    [Fact]
    public void Hours_may_not_exceed_the_slot()
    {
        Should.Throw<DomainRuleViolationException>(() => Log(Hour(9), Hour(10), hours: 4m));
    }

    [Fact]
    public void Project_work_needs_a_project()
    {
        // An hour of BUILD against nothing cannot be costed, reported, or drawn on a board.
        Should.Throw<DomainRuleViolationException>(() => Log(type: "project-build", projectId: null));
    }

    [Fact]
    public void Non_project_work_refuses_a_project()
    {
        // Silently keeping it would put recruitment hours into a project's cost in S11 with nobody the wiser.
        Should.Throw<DomainRuleViolationException>(
            () => Log(type: "recruitment-admin", projectId: Project));
    }

    [Fact]
    public void An_iteration_link_needs_its_project()
    {
        Should.Throw<DomainRuleViolationException>(() => Log(
            type: "quality-of-life",
            projectId: null,
            iterationId: Guid.CreateVersion7()));
    }

    [Fact]
    public void An_unknown_activity_type_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => Log(type: "sabbatical"));
    }

    [Fact]
    public void A_slot_must_end_after_it_starts()
    {
        Should.Throw<DomainRuleViolationException>(() => new TimeSlot(Hour(12), Hour(9)));
        Should.Throw<DomainRuleViolationException>(() => new TimeSlot(Hour(9), Hour(9)));
    }

    [Fact]
    public void A_slot_cannot_span_more_than_a_day()
    {
        // Almost always a date-picker mistake, and it would trip the weekly guardrail on one bad entry rather
        // than on a real pattern of overtime.
        Should.Throw<DomainRuleViolationException>(() => new TimeSlot(Hour(9), Hour(9).AddDays(2)));
    }

    [Fact]
    public void Hours_are_quantised_to_the_quarter()
    {
        // Free decimals turn a weekly total of 35 into 34.999999 often enough to matter to a guardrail.
        new WorkHours(1.1m).Value.ShouldBe(1m);
        new WorkHours(1.13m).Value.ShouldBe(1.25m);
        new WorkHours(0.4m).Value.ShouldBe(0.5m);
    }

    [Fact]
    public void An_entry_stamps_the_iso_week_of_its_start()
    {
        var entry = Log(start: new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero), end: new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

        entry.Week.ShouldBe(new IsoWeek(2026, 34));
    }

    [Fact]
    public void The_iso_week_of_a_january_day_may_belong_to_the_previous_year()
    {
        // 1 January 2027 is a Friday, so ISO puts it in week 53 of 2026. Treating it as week 1 of 2027 would split
        // one working week across two guardrail buckets.
        IsoWeek.Of(new DateOnly(2027, 1, 1)).ShouldBe(new IsoWeek(2026, 53));
    }

    [Fact]
    public void An_actual_supersedes_a_plan_without_erasing_it()
    {
        var plan = Log(kind: ActivityKind.Planned, type: "project-build", projectId: Project, hours: 4m,
            start: Hour(9), end: Hour(13));
        var actual = Log(kind: ActivityKind.Actual, type: "project-run", projectId: Project, hours: 6m,
            start: Hour(9), end: Hour(15));

        actual.Reconcile(plan, Person, Now);

        // "We planned four hours of BUILD and spent six on RUN" is the single most useful thing this module can
        // say, and it is only answerable while both numbers still exist.
        actual.SupersedesEntryId.ShouldBe(plan.Id);
        plan.Reconciled.ShouldBeTrue();
        plan.Hours.ShouldBe(4m);
        plan.ActivityTypeCode.ShouldBe("project-build");
    }

    [Fact]
    public void A_plan_cannot_be_reconciled_twice()
    {
        var plan = Log(kind: ActivityKind.Planned);
        var first = Log(kind: ActivityKind.Actual);
        var second = Log(kind: ActivityKind.Actual);

        first.Reconcile(plan, Person, Now);

        Should.Throw<DomainRuleViolationException>(() => second.Reconcile(plan, Person, Now));
    }

    [Fact]
    public void Only_an_actual_can_reconcile_and_only_against_a_plan()
    {
        var plan = Log(kind: ActivityKind.Planned);
        var otherPlan = Log(kind: ActivityKind.Planned);
        var actual = Log(kind: ActivityKind.Actual);

        Should.Throw<DomainRuleViolationException>(() => plan.Reconcile(otherPlan, Person, Now));
        Should.Throw<DomainRuleViolationException>(() => actual.Reconcile(actual, Person, Now));
    }

    [Fact]
    public void An_entry_cannot_supersede_someone_elses_plan()
    {
        var theirPlan = Log(kind: ActivityKind.Planned, personId: Colleague);
        var mine = Log(kind: ActivityKind.Actual);

        Should.Throw<DomainRuleViolationException>(() => mine.Reconcile(theirPlan, Person, Now));
    }

    [Fact]
    public void Logging_raises_an_event_carrying_the_week()
    {
        var entry = Log();

        var logged = entry.DomainEvents.OfType<ActivityLogged>().ShouldHaveSingleItem();

        logged.Week.ShouldBe(IsoWeek.Of(entry.SlotStart));
        logged.Hours.ShouldBe(entry.Hours);
    }

    [Fact]
    public void Reconciling_raises_an_event_carrying_both_sides()
    {
        var plan = Log(kind: ActivityKind.Planned, hours: 4m, start: Hour(9), end: Hour(13));
        var actual = Log(kind: ActivityKind.Actual, hours: 6m, start: Hour(9), end: Hour(15));

        actual.ClearDomainEvents();
        actual.Reconcile(plan, Person, Now);

        var reconciled = actual.DomainEvents.OfType<ActivityReconciled>().ShouldHaveSingleItem();

        reconciled.PlannedHours.ShouldBe(4m);
        reconciled.ActualHours.ShouldBe(6m);
    }

    [Fact]
    public void Amending_moves_the_week_when_the_slot_moves()
    {
        var entry = Log(start: Hour(9), end: Hour(12));

        entry.Amend(
            Taxonomy,
            "quality-of-life",
            projectId: null,
            iterationId: null,
            new TimeSlot(
                new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 26, 11, 0, 0, TimeSpan.Zero)),
            hours: null,
            note: null,
            Person,
            Now);

        entry.Week.ShouldBe(new IsoWeek(2026, 35));
        entry.Hours.ShouldBe(2m);
    }

    [Fact]
    public void A_planned_slot_starts_with_no_stated_progress()
    {
        // Null rather than zero, because "nobody has said" and "started and nothing done" are different answers
        // and the board draws them differently: one falls back to plan-versus-actual, the other does not.
        var entry = Log(kind: ActivityKind.Planned);

        entry.PercentComplete.ShouldBeNull();
    }

    [Fact]
    public void Progress_can_be_set_on_a_planned_slot()
    {
        var entry = Log(kind: ActivityKind.Planned);

        entry.SetProgress(40, Person, Now);

        entry.PercentComplete.ShouldBe(40);
    }

    [Fact]
    public void Progress_can_be_cleared()
    {
        var entry = Log(kind: ActivityKind.Planned);

        entry.SetProgress(40, Person, Now);
        entry.SetProgress(null, Person, Now);

        entry.PercentComplete.ShouldBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_outside_nought_to_a_hundred_is_refused(int percent)
    {
        var entry = Log(kind: ActivityKind.Planned);

        Should.Throw<DomainRuleViolationException>(() => entry.SetProgress(percent, Person, Now));
    }

    [Fact]
    public void An_actual_carries_no_progress()
    {
        // An actual is a claim about time already spent; it is finished by definition, and a percentage on it
        // would be a second answer to a question the hours have already settled.
        var entry = Log(kind: ActivityKind.Actual);

        Should.Throw<DomainRuleViolationException>(() => entry.SetProgress(50, Person, Now));
    }

    private static DateTimeOffset Hour(int hour) => new(2026, 8, 19, hour, 0, 0, TimeSpan.Zero);

    private static ActivityEntry Log(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string type = "quality-of-life",
        Guid? projectId = null,
        Guid? iterationId = null,
        ActivityKind kind = ActivityKind.Actual,
        decimal? hours = null,
        Guid? personId = null) =>
        ActivityEntry.Log(
            personId ?? Person,
            Unit,
            Department,
            Taxonomy,
            type,
            projectId,
            iterationId,
            kind,
            ActivitySource.Manual,
            externalRef: null,
            new TimeSlot(start ?? Hour(9), end ?? Hour(12)),
            hours is { } value ? new WorkHours(value) : null,
            note: null,
            personId ?? Person,
            Now);
}
