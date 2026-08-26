using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Domain;

namespace Cracra.Tests.Unit.Meetings;

/// <summary>
/// The CR's own rules (v2 §07.1), away from a database.
/// </summary>
/// <remarks>
/// Meetings is a 2-layer module and most of it is covered end to end, but these guards are worth isolating: they
/// are the difference between a compte-rendu that is a record and one that is a scratchpad anybody may rewrite
/// after the fact.
/// </remarks>
public sealed class MinutesRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Occurrence = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly Guid Series = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private static readonly Guid Author = Guid.Parse("11111111-0000-0000-0000-000000000003");

    private static readonly Guid Owner = Guid.Parse("11111111-0000-0000-0000-000000000004");

    [Fact]
    public void Minutes_open_against_their_occurrence()
    {
        var minutes = Open();

        minutes.OccurrenceId.ShouldBe(Occurrence);
        minutes.SeriesId.ShouldBe(Series);
        minutes.Published.ShouldBeFalse();
    }

    [Fact]
    public void A_level_nobody_defined_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => Open(level: "directorate"));
    }

    [Fact]
    public void Only_a_cross_node_CR_keeps_its_target_list()
    {
        var targets = new[] { Guid.NewGuid(), Guid.NewGuid() };

        Open(level: MeetingLevels.CrossNode, scopeIds: targets).ScopeIds.ShouldBe(targets);

        // A node CR carrying a stale list would publish itself to nodes nobody meant, which is the one failure
        // mode of a distribution list that nobody notices until it is somebody else's minutes.
        Open(level: MeetingLevels.Node, scopeIds: targets).ScopeIds.ShouldBeEmpty();
    }

    [Fact]
    public void Somebody_in_both_lists_was_present()
    {
        var minutes = Open();

        minutes.Amend(null, [Owner], [Owner, Author], null, Now);

        minutes.Attendees.ShouldBe([Owner]);
        minutes.Absentees.ShouldBe([Author]);
    }

    [Fact]
    public void An_empty_CR_is_not_worth_distributing()
    {
        Should.Throw<DomainRuleViolationException>(() => Open().Publish(Now));
    }

    [Fact]
    public void A_summary_alone_is_enough_to_publish()
    {
        var minutes = Open();

        minutes.Amend(null, null, null, "Nothing blocking.", Now);
        minutes.Publish(Now);

        minutes.Published.ShouldBeTrue();
        minutes.PublishedAt.ShouldBe(Now);
    }

    [Fact]
    public void Publishing_twice_is_refused_rather_than_ignored()
    {
        var minutes = Published();

        Should.Throw<DomainRuleViolationException>(() => minutes.Publish(Now));
    }

    [Fact]
    public void A_decision_cannot_be_added_after_publication()
    {
        var minutes = Published();

        Should.Throw<DomainRuleViolationException>(() => minutes.Decide("Ship it", null, "le COPIL", Now));
    }

    [Fact]
    public void An_action_can_be_added_after_publication()
    {
        // Deliberately asymmetric with a decision. A decision is what the room agreed; work carries on.
        var minutes = Published();

        var action = minutes.Assign("Draft the note", Owner, null, ActionLinkTypes.None, null, Now);

        action.Status.ShouldBe(ActionStatuses.Open);
    }

    [Fact]
    public void A_decision_has_to_say_what_was_decided()
    {
        Should.Throw<DomainRuleViolationException>(() => Open().Decide("  ", null, null, Now));
    }

    [Fact]
    public void An_action_nobody_owns_is_a_wish()
    {
        Should.Throw<DomainRuleViolationException>(
            () => Open().Assign("Do the thing", Guid.Empty, null, ActionLinkTypes.None, null, Now));
    }

    [Fact]
    public void An_action_pointed_at_a_kind_nobody_defined_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(
            () => Open().Assign("Do the thing", Owner, null, "invoice", Guid.NewGuid(), Now));
    }

    [Fact]
    public void An_action_linked_to_a_problem_needs_the_problem()
    {
        Should.Throw<DomainRuleViolationException>(
            () => Open().Assign("Fix the pain", Owner, null, ActionLinkTypes.Problem, null, Now));
    }

    [Fact]
    public void An_unlinked_action_drops_whatever_id_came_with_it()
    {
        var action = Open().Assign("Do the thing", Owner, null, ActionLinkTypes.None, Guid.NewGuid(), Now);

        action.LinkId.ShouldBeNull();
    }

    [Fact]
    public void Overdue_is_open_and_past_its_date()
    {
        var minutes = Open();
        var due = new DateOnly(2026, 8, 20);

        var action = minutes.Assign("Do the thing", Owner, due, ActionLinkTypes.None, null, Now);

        action.IsOverdueAt(due.AddDays(1)).ShouldBeTrue();
        action.IsOverdueAt(due).ShouldBeFalse();

        action.Settle(ActionStatuses.Done, Now);

        // A closed action is late history, not outstanding work, and a badge that counts it never reaches zero.
        action.IsOverdueAt(due.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void An_action_undated_is_never_overdue()
    {
        var action = Open().Assign("Someday", Owner, null, ActionLinkTypes.None, null, Now);

        action.IsOverdueAt(new DateOnly(2030, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_status_nobody_defined_is_refused()
    {
        var action = Open().Assign("Do the thing", Owner, null, ActionLinkTypes.None, null, Now);

        Should.Throw<DomainRuleViolationException>(() => action.Settle("parked", Now));
    }

    [Fact]
    public void The_linked_work_resolving_closes_the_action_once()
    {
        var problem = Guid.NewGuid();
        var action = Open().Assign("Fix the pain", Owner, null, ActionLinkTypes.Problem, problem, Now);

        action.CloseBecauseLinkResolved(Now).ShouldBeTrue();
        action.Status.ShouldBe(ActionStatuses.Done);

        // The caller is an event handler, and a duplicate delivery must not raise or re-close.
        action.CloseBecauseLinkResolved(Now).ShouldBeFalse();
    }

    [Fact]
    public void A_dropped_action_is_not_reopened_by_its_link_resolving()
    {
        var action = Open().Assign("Fix the pain", Owner, null, ActionLinkTypes.Problem, Guid.NewGuid(), Now);

        action.Settle(ActionStatuses.Dropped, Now);

        action.CloseBecauseLinkResolved(Now).ShouldBeFalse();
        action.Status.ShouldBe(ActionStatuses.Dropped);
    }

    [Fact]
    public void Amending_an_action_leaves_out_what_the_patch_did_not_mention()
    {
        var minutes = Open();
        var due = new DateOnly(2026, 9, 1);
        var action = minutes.Assign("Draft the note", Owner, due, ActionLinkTypes.None, null, Now);

        action.Retitle(null, null, null, Now);

        action.Title.ShouldBe("Draft the note");
        action.OwnerPersonId.ShouldBe(Owner);
        action.Due.ShouldBe(due);
    }

    [Fact]
    public void An_action_that_is_not_on_these_minutes_is_a_404()
    {
        Should.Throw<ResourceNotFoundException>(() => Open().Action(Guid.NewGuid()));
    }

    private static MeetingMinutes Open(
        string level = MeetingLevels.Node,
        Guid[]? scopeIds = null) =>
        MeetingMinutes.Open(
            Occurrence,
            Series,
            level,
            MeetingScopeTypes.Department,
            Guid.Parse("11111111-0000-0000-0000-00000000000a"),
            scopeIds ?? [],
            Now,
            Author,
            Now);

    private static MeetingMinutes Published()
    {
        var minutes = Open();

        minutes.Amend(null, null, null, "Nothing blocking.", Now);
        minutes.Publish(Now);

        return minutes;
    }
}
