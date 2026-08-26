using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Tests.Unit.Portfolio;

/// <summary>
/// The rules the catalog adds to the portfolio aggregate (v2 §03).
/// </summary>
/// <remarks>
/// These are the guards that decide whether a card can be trusted. "Awaiting v2" with nothing behind it and a
/// dependency graph with a loop in it are both states that look fine in a list and are wrong the moment anybody
/// acts on them, which is exactly the kind of thing worth pinning before it reaches a database.
/// </remarks>
public sealed class CatalogRulesTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Author = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_item_can_be_created_with_only_a_type_a_name_and_an_owner()
    {
        // §03.3: steps 1 and 2 of the wizard produce a real card. Everything else is fillable later, which is what
        // makes the short path usable without making the long one a different entity.
        var item = Platform();

        item.Code.ShouldBe("K8S");
        item.Type.ShouldBe(ItemType.Platform);
        item.State.ShouldBe(PortfolioState.Considered);
        item.OwnerNodeId.ShouldBe(Owner);
    }

    [Fact]
    public void A_platform_defaults_to_no_category_rather_than_an_invented_one()
    {
        // Categories are the deployment's vocabulary (§03.1). Guessing one here would put a word in the code that
        // only one organisation uses.
        Platform().Category.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_item_without_a_name_is_refused(string name) =>
        Should.Throw<DomainRuleViolationException>(() => Create(name: name));

    [Fact]
    public void An_item_without_an_owning_node_is_refused() =>
        Should.Throw<DomainRuleViolationException>(() => Create(owner: Guid.Empty));

    [Fact]
    public void A_negative_estimate_is_refused() =>
        Should.Throw<DomainRuleViolationException>(() => Create(estimate: -1m));

    [Fact]
    public void An_unknown_type_is_refused() =>
        Should.Throw<DomainRuleViolationException>(() => ItemTypes.Parse("gadget"));

    // --- Awaiting v2 ---------------------------------------------------------------------------------------------

    [Fact]
    public void Awaiting_a_next_version_needs_a_queued_epic_behind_it()
    {
        // The whole point of the state. A pill saying "Awaiting v2" over an empty backlog is a wish, and making
        // the backlog the precondition means the card can never claim more than the list behind it.
        var item = Live();

        var refused = Should.Throw<DomainRuleViolationException>(
            () => item.AwaitNextVersion("v2", Author, Now));

        refused.Message.ShouldContain("epic");
        item.State.ShouldBe(PortfolioState.Active);
    }

    [Fact]
    public void An_idea_nobody_committed_to_does_not_count_as_a_queued_version()
    {
        var item = Live();

        item.AddEpic("Maybe someday", null, EpicStatus.Idea, null, Author, Now);

        Should.Throw<DomainRuleViolationException>(() => item.AwaitNextVersion("v2", Author, Now));
    }

    [Fact]
    public void A_deferred_epic_is_enough_to_queue_a_next_version()
    {
        var item = Live();

        item.AddEpic("Bulk import", null, EpicStatus.Deferred, "v2", Author, Now);

        item.AwaitNextVersion("v2", Author, Now);

        item.State.ShouldBe(PortfolioState.AwaitingVnext);
        item.AwaitingVersion.ShouldBe("v2");
    }

    [Fact]
    public void Only_a_live_item_can_be_waiting_for_its_next_version()
    {
        // Considered and committed things have no version in production to be the one after.
        var item = Platform();

        item.AddEpic("Bulk import", null, EpicStatus.Deferred, "v2", Author, Now);

        Should.Throw<DomainRuleViolationException>(() => item.AwaitNextVersion("v2", Author, Now));
    }

    [Fact]
    public void Awaiting_a_next_version_needs_the_version_label()
    {
        var item = Live();

        item.AddEpic("Bulk import", null, EpicStatus.Deferred, null, Author, Now);

        Should.Throw<DomainRuleViolationException>(() => item.AwaitNextVersion("  ", Author, Now));
    }

    [Fact]
    public void Awaiting_a_next_version_is_a_forward_step_and_not_retirement()
    {
        // Ordered between active and déphasé, so reverting drops the queued version rather than resurrecting a
        // retired item. The item is still in production and still costs money.
        ((int)PortfolioState.AwaitingVnext).ShouldBeGreaterThan((int)PortfolioState.Active);
        ((int)PortfolioState.AwaitingVnext).ShouldBeLessThan((int)PortfolioState.Dephase);
    }

    // --- Dependencies --------------------------------------------------------------------------------------------

    [Fact]
    public void An_item_cannot_depend_on_itself()
    {
        var item = Platform();

        Should.Throw<DomainRuleViolationException>(
            () => item.DependOn(item.Id, DependencyKind.Consumes, null, wouldCycle: true, Author, Now));
    }

    [Fact]
    public void The_same_dependency_is_not_recorded_twice()
    {
        var item = Platform();
        var other = Guid.CreateVersion7();

        item.DependOn(other, DependencyKind.Consumes, null, wouldCycle: false, Author, Now);

        Should.Throw<DomainRuleViolationException>(
            () => item.DependOn(other, DependencyKind.Integrates, null, wouldCycle: false, Author, Now));
    }

    [Fact]
    public void A_dependency_that_would_close_a_loop_is_refused()
    {
        var item = Platform();

        var refused = Should.Throw<DomainRuleViolationException>(
            () => item.DependOn(Guid.CreateVersion7(), DependencyKind.Consumes, null, true, Author, Now));

        refused.Message.ShouldContain("loop");
    }

    [Fact]
    public void A_direct_loop_is_a_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        DependencyGraph.WouldCycle([new DependencyEdge(b, a)], a, b).ShouldBeTrue();
    }

    [Fact]
    public void A_loop_three_items_long_is_still_a_cycle()
    {
        // The case a "does the other one point straight back at me" check would miss, and the reason the walk
        // exists at all: A consumes B, B consumes C, and somebody adds C consumes A.
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        DependencyEdge[] edges = [new(a, b), new(b, c)];

        DependencyGraph.WouldCycle(edges, c, a).ShouldBeTrue();
    }

    [Fact]
    public void A_diamond_is_not_a_cycle()
    {
        // Two items consuming the same platform is the shape the catalog exists to show, not one to refuse.
        var platform = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        DependencyEdge[] edges = [new(first, platform)];

        DependencyGraph.WouldCycle(edges, second, platform).ShouldBeFalse();
    }

    [Fact]
    public void An_unrelated_graph_does_not_make_everything_a_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        DependencyEdge[] edges = [new(Guid.CreateVersion7(), Guid.CreateVersion7())];

        DependencyGraph.WouldCycle(edges, a, b).ShouldBeFalse();
    }

    // --- Team ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Somebody_already_on_the_item_is_not_added_twice()
    {
        var item = Platform();
        var person = Guid.CreateVersion7();

        item.AddMember(person, Owner, null, 50, Today, null, Author, Now);

        Should.Throw<DomainRuleViolationException>(
            () => item.AddMember(person, Owner, null, 50, Today, null, Author, Now));
    }

    [Fact]
    public void Somebody_who_left_can_join_again()
    {
        // A contributor coming back is ordinary. Refusing it would mean editing history to record the present.
        var item = Platform();
        var person = Guid.CreateVersion7();

        item.AddMember(person, Owner, null, null, Today, null, Author, Now);
        item.RemoveMember(person, Today, Author, Now);

        item.AddMember(person, Owner, null, null, Today, null, Author, Now);

        item.Members.Count.ShouldBe(2);
    }

    [Fact]
    public void An_allocation_outside_a_percentage_is_refused() =>
        Should.Throw<DomainRuleViolationException>(
            () => ItemMember.Join(Guid.CreateVersion7(), Guid.CreateVersion7(), Owner, null, 140, Today, null));

    [Fact]
    public void A_membership_cannot_end_before_it_starts() =>
        Should.Throw<DomainRuleViolationException>(
            () => ItemMember.Join(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Owner,
                null,
                null,
                Today,
                Today.AddDays(-1)));

    [Fact]
    public void A_member_records_the_node_they_contributed_from()
    {
        // Carried on the membership rather than read from the person, so the grouped view survives somebody
        // moving branch: what it records is who contributed from where, not where they sit today.
        var contributing = Guid.CreateVersion7();
        var item = Platform();

        item.AddMember(Guid.CreateVersion7(), contributing, null, null, Today, null, Author, Now);

        item.Members.Single().NodeId.ShouldBe(contributing);
    }

    private static DateOnly Today => new(2026, 8, 24);

    private static PortfolioItem Platform() => Create();

    /// <summary>An item in production, which is the only state a next version can be queued behind.</summary>
    private static PortfolioItem Live()
    {
        var item = Create();

        item.Commit(Guid.CreateVersion7(), "Approved at the COPIL.", Author, Now);
        item.AddIteration("Sprint 1", IterationLength.TwoWeeks, Today, null, Author, Now);
        item.Activate(hasTeam: true, Author, Now);

        return item;
    }

    private static PortfolioItem Create(
        string name = "K8s",
        Guid? owner = null,
        decimal? estimate = null) =>
        PortfolioItem.Create(
            "K8S",
            name,
            ItemType.Platform,
            null,
            ItemClassification.Mixed,
            owner ?? Owner,
            null,
            null,
            null,
            estimate,
            null,
            100,
            Author,
            Now);
}
