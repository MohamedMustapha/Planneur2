using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Tests.Unit.Portfolio;

/// <summary>
/// The lifecycle state machine.
/// </summary>
/// <remarks>
/// The point of S4 is that a portfolio item cannot reach a state without having earned it. Every guard is tested
/// here, without a database, because guards are cheap to get subtly wrong and expensive to discover wrong.
/// </remarks>
public sealed class PortfolioItemTests
{
    private static readonly Guid Head = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Dept = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 19);

    [Fact]
    public void A_new_item_starts_as_a_candidate_with_no_project()
    {
        var item = Considered();

        item.State.ShouldBe(PortfolioState.Considered);
        item.ProjectId.ShouldBeNull();
        item.ConsideredAt.ShouldBe(Now);
    }

    [Fact]
    public void A_candidate_needs_a_name_and_a_sponsoring_department()
    {
        Should.Throw<DomainRuleViolationException>(
            () => PortfolioItem.Consider("CODE", "  ", 1, Dept, null, Head, Now));

        Should.Throw<DomainRuleViolationException>(
            () => PortfolioItem.Consider("CODE", "Refonte", 1, Guid.Empty, null, Head, Now));
    }

    [Fact]
    public void Committing_records_the_project_and_the_reason()
    {
        var item = Considered();

        item.Commit(ProjectId, "Board approved the budget.", Head, Now);

        item.State.ShouldBe(PortfolioState.Committed);
        item.ProjectId.ShouldBe(ProjectId);
        item.DecisionNotes.ShouldBe("Board approved the budget.");
        item.CommittedAt.ShouldBe(Now);
    }

    [Fact]
    public void Committing_without_a_decision_note_is_refused()
    {
        var item = Considered();

        // The note is the whole audit value of the transition; a commitment nobody can explain later is the case
        // this rule exists to prevent.
        Should.Throw<DomainRuleViolationException>(() => item.Commit(ProjectId, "   ", Head, Now));
    }

    [Fact]
    public void States_cannot_be_skipped()
    {
        var item = Considered();

        // Considered straight to active would leave no committed_at stamp, and when the commitment was taken is
        // exactly what the portfolio exists to record.
        Should.Throw<DomainRuleViolationException>(() => item.Activate(hasTeam: true, Head, Now));
    }

    [Fact]
    public void Activating_needs_a_plan()
    {
        var item = Committed();

        Should.Throw<DomainRuleViolationException>(() => item.Activate(hasTeam: true, Head, Now));
    }

    [Fact]
    public void Activating_needs_a_team()
    {
        var item = Committed();
        item.AddIteration("Sprint 1", IterationLength.TwoWeeks, Today, null, Head, Now);

        Should.Throw<DomainRuleViolationException>(() => item.Activate(hasTeam: false, Head, Now));
    }

    [Fact]
    public void Activating_starts_the_first_planned_iteration()
    {
        var item = Active();

        // An active item whose iteration strip shows nothing running would be a status, not a state.
        item.CurrentIteration!.State.ShouldBe(IterationState.Active);
        item.CurrentIteration.Sequence.ShouldBe(1);
    }

    [Fact]
    public void Closing_an_iteration_starts_the_next_planned_one()
    {
        var item = Active();
        item.AddIteration("Sprint 2", IterationLength.TwoWeeks, Today.AddDays(14), null, Head, Now);

        item.CloseIteration(item.CurrentIteration!.Id, Head, Now);

        item.Iterations.Single(iteration => iteration.Sequence == 1).State.ShouldBe(IterationState.Done);
        item.CurrentIteration!.Sequence.ShouldBe(2);
        item.CurrentIteration.State.ShouldBe(IterationState.Active);
    }

    [Fact]
    public void Archiving_cancels_every_open_iteration()
    {
        var item = Active();
        item.AddIteration("Sprint 2", IterationLength.TwoWeeks, Today.AddDays(14), null, Head, Now);

        item.Archive("Replaced by the new platform.", Head, Now);

        item.State.ShouldBe(PortfolioState.Dephase);
        item.IsArchived.ShouldBeTrue();

        // Otherwise S6 would keep drawing live work on something nobody is delivering.
        item.Iterations.ShouldAllBe(iteration => iteration.State == IterationState.Cancelled);
    }

    [Fact]
    public void An_archived_item_is_read_only()
    {
        var item = Active();
        item.Archive("Done with it.", Head, Now);

        Should.Throw<DomainRuleViolationException>(
            () => item.AddIteration("Sprint 2", IterationLength.OneWeek, Today, null, Head, Now));

        Should.Throw<DomainRuleViolationException>(() => item.Archive("Again.", Head, Now));
    }

    [Fact]
    public void Reverting_moves_backwards_and_clears_the_stamps_it_undoes()
    {
        var item = Active();

        item.Revert(PortfolioState.Committed, "Archived by mistake; still funded.", Head, Now);

        item.State.ShouldBe(PortfolioState.Committed);
        item.ActivatedAt.ShouldBeNull();

        // The commitment itself was not undone, so its stamp survives.
        item.CommittedAt.ShouldBe(Now);
    }

    [Fact]
    public void Reverting_forwards_is_refused()
    {
        var item = Committed();

        // Revert is the escape hatch for going back; letting it go forward would route around every guard the
        // forward transitions apply.
        Should.Throw<DomainRuleViolationException>(
            () => item.Revert(PortfolioState.Active, "Just make it active.", Head, Now));
    }

    [Fact]
    public void Every_transition_raises_an_event()
    {
        var item = Considered();
        item.ClearDomainEvents();

        item.Commit(ProjectId, "Approved.", Head, Now);
        item.AddIteration("Sprint 1", IterationLength.OneWeek, Today, null, Head, Now);
        item.Activate(hasTeam: true, Head, Now);
        item.Archive("Retired.", Head, Now);

        item.DomainEvents.OfType<ItemCommitted>().ShouldHaveSingleItem();
        item.DomainEvents.OfType<ItemActivated>().ShouldHaveSingleItem();
        item.DomainEvents.OfType<ItemArchived>().ShouldHaveSingleItem();
    }

    private static PortfolioItem Considered() =>
        PortfolioItem.Consider("REFONTEPORTA", "Refonte du portail", 10, Dept, "Raised at the steering committee.", Head, Now);

    private static PortfolioItem Committed()
    {
        var item = Considered();
        item.Commit(ProjectId, "Board approved the budget.", Head, Now);

        return item;
    }

    private static PortfolioItem Active()
    {
        var item = Committed();
        item.AddIteration("Sprint 1", IterationLength.TwoWeeks, Today, null, Head, Now);
        item.Activate(hasTeam: true, Head, Now);

        return item;
    }
}
