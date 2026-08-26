using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Problems.Domain;

namespace Cracra.Tests.Unit.Problems;

/// <summary>
/// The problem lifecycle and its ranking (v2 §05).
/// </summary>
/// <remarks>
/// A backlog people trust is one where the guards actually hold: a declined problem cannot quietly become a
/// project, a duplicate has to name what it duplicates, and one person's enthusiasm is one vote. All three look
/// like bookkeeping and all three are the difference between an intake pipeline and a suggestion box.
/// </remarks>
public sealed class ProblemLifecycleTests
{
    private static readonly Guid Node = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Reporter = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_filed_problem_starts_new()
    {
        Filed().Status.ShouldBe(ProblemStatus.New);
    }

    [Fact]
    public void A_problem_needs_a_title_somebody_would_recognise() =>
        Should.Throw<DomainRuleViolationException>(() => Filed(title: "  "));

    [Fact]
    public void A_problem_needs_the_node_it_belongs_to() =>
        Should.Throw<DomainRuleViolationException>(() => Filed(node: Guid.Empty));

    // --- Triage ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Accepting_moves_it_on()
    {
        var problem = Filed();

        problem.Triage(ProblemStatuses.Accepted, null, null, Now);

        problem.Status.ShouldBe(ProblemStatus.Accepted);
    }

    [Fact]
    public void Declining_needs_a_reason()
    {
        // Somebody took the trouble to file this. A refusal with no reason is how people stop filing.
        var problem = Filed();

        Should.Throw<DomainRuleViolationException>(
            () => problem.Triage(ProblemStatuses.Declined, null, null, Now));
    }

    [Fact]
    public void Marking_a_duplicate_needs_the_problem_it_duplicates()
    {
        var problem = Filed();

        Should.Throw<DomainRuleViolationException>(
            () => problem.Triage(ProblemStatuses.Duplicate, null, null, Now));
    }

    [Fact]
    public void A_problem_cannot_duplicate_itself()
    {
        var problem = Filed();

        Should.Throw<DomainRuleViolationException>(
            () => problem.Triage(ProblemStatuses.Duplicate, null, problem.Id, Now));
    }

    [Fact]
    public void Triage_happens_once()
    {
        var problem = Filed();

        problem.Triage(ProblemStatuses.Accepted, null, null, Now);

        Should.Throw<DomainRuleViolationException>(
            () => problem.Triage(ProblemStatuses.Declined, "Changed my mind.", null, Now));
    }

    [Fact]
    public void An_unknown_decision_is_refused()
    {
        var problem = Filed();

        Should.Throw<DomainRuleViolationException>(() => problem.Triage("maybe", null, null, Now));
    }

    // --- Conversion -----------------------------------------------------------------------------------------------

    [Fact]
    public void An_accepted_problem_becomes_an_item_and_keeps_the_link()
    {
        var problem = Filed();
        var itemId = Guid.CreateVersion7();

        problem.Triage(ProblemStatuses.Accepted, null, null, Now);
        problem.Convert(itemId, Now);

        problem.Status.ShouldBe(ProblemStatus.Converted);
        problem.ConvertedItemId.ShouldBe(itemId);
    }

    [Fact]
    public void A_declined_problem_cannot_be_converted()
    {
        // The no-shadow-IT rule in one line: converting is how a pain becomes work somebody owns, and letting a
        // declined one through would route around the decision a head already took.
        var problem = Filed();

        problem.Triage(ProblemStatuses.Declined, "Out of scope.", null, Now);

        Should.Throw<DomainRuleViolationException>(() => problem.Convert(Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void A_duplicate_cannot_be_converted()
    {
        var problem = Filed();

        problem.Triage(ProblemStatuses.Duplicate, null, Guid.CreateVersion7(), Now);

        Should.Throw<DomainRuleViolationException>(() => problem.Convert(Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void An_untriaged_problem_cannot_be_converted()
    {
        Should.Throw<DomainRuleViolationException>(() => Filed().Convert(Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void Only_something_accepted_or_converted_can_be_resolved()
    {
        Should.Throw<DomainRuleViolationException>(() => Filed().Resolve(null, Now));
    }

    // --- Votes ----------------------------------------------------------------------------------------------------

    [Fact]
    public void One_vote_per_person()
    {
        var problem = Filed();

        problem.Vote(Other, Now).ShouldBeTrue();
        problem.Vote(Other, Now).ShouldBeFalse();

        problem.VoteCount.ShouldBe(1);
    }

    [Fact]
    public void A_second_press_is_not_an_error()
    {
        // The button is a "me too". Somebody pressing it twice still means it, and failing the request would be a
        // worse answer than saying it did not count again.
        var problem = Filed();

        problem.Vote(Other, Now);

        Should.NotThrow(() => problem.Vote(Other, Now));
    }

    [Fact]
    public void A_resolved_problem_stops_taking_votes_and_proposals()
    {
        var problem = Filed();

        problem.Triage(ProblemStatuses.Accepted, null, null, Now);
        problem.Resolve("Fixed by the new form.", Now);

        Should.Throw<DomainRuleViolationException>(() => problem.Vote(Other, Now));
        Should.Throw<DomainRuleViolationException>(() => problem.Propose(Other, "Try this.", null, Now));
    }

    [Fact]
    public void A_declined_problem_stops_taking_comments()
    {
        var problem = Filed();

        problem.Triage(ProblemStatuses.Declined, "Not ours.", null, Now);

        Should.Throw<DomainRuleViolationException>(() => problem.Comment(Other, "But...", Now));
    }

    [Fact]
    public void An_empty_proposal_says_nothing()
    {
        var problem = Filed();

        Should.Throw<DomainRuleViolationException>(() => problem.Propose(Other, "   ", null, Now));
    }

    // --- Ranking --------------------------------------------------------------------------------------------------

    [Fact]
    public void Impact_is_time_lost_times_how_often_times_how_many()
    {
        // Half an hour, weekly, for four people.
        var problem = Filed(timeLoss: 0.5m, frequency: ImpactFrequency.Weekly, affected: 4);

        problem.AnnualHoursLost.ShouldBe(0.5m * 46m * 4);
    }

    [Fact]
    public void A_daily_pain_outranks_an_occasional_one_of_the_same_size()
    {
        var daily = Filed(timeLoss: 1m, frequency: ImpactFrequency.Daily, affected: 1);
        var occasional = Filed(timeLoss: 1m, frequency: ImpactFrequency.Occasional, affected: 1);

        daily.AnnualHoursLost.ShouldBeGreaterThan(occasional.AnnualHoursLost);
    }

    [Fact]
    public void A_pain_nobody_quantified_still_ranks_at_zero_rather_than_failing()
    {
        // Plenty of real irritants arrive without numbers. Demanding them at the form would filter out exactly the
        // people the intake is for.
        Filed(timeLoss: null).AnnualHoursLost.ShouldBe(0m);
    }

    [Fact]
    public void One_person_is_assumed_when_nobody_said_how_many()
    {
        Filed(timeLoss: 2m, frequency: ImpactFrequency.Weekly, affected: null)
            .AnnualHoursLost.ShouldBe(2m * 46m);
    }

    private static Problem Filed(
        string title = "Too many manual steps to close a ticket",
        Guid? node = null,
        decimal? timeLoss = 1m,
        ImpactFrequency frequency = ImpactFrequency.Weekly,
        int? affected = 1) =>
        Problem.File(
            "PB-2026-001",
            title,
            "Six clicks where one would do.",
            ProblemCategories.Tooling,
            OriginScopes.Node,
            node ?? Node,
            node ?? Node,
            Reporter,
            timeLoss,
            frequency,
            affected,
            Now);
}
