using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Kudos.Domain;

namespace Cracra.Tests.Unit.Kudos;

/// <summary>
/// The Kudo aggregate: eligibility, the monthly cap, pricing, and the badge a kudo can cross.
/// </summary>
public sealed class KudoTests
{
    private static readonly Guid Giver = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Receiver = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_kudo_records_who_thanked_whom_and_what_for()
    {
        var kudo = Give(category: "cleanup", message: "Cleared the build warnings nobody else would touch.");

        kudo.FromPersonId.ShouldBe(Giver);
        kudo.ToPersonId.ShouldBe(Receiver);
        kudo.Category.ShouldBe("cleanup");
        kudo.Message.ShouldBe("Cleared the build warnings nobody else would touch.");
        kudo.Month.ShouldBe(new KudoMonth(2026, 8));
    }

    [Fact]
    public void Nobody_may_thank_themselves()
    {
        Should.Throw<DomainRuleViolationException>(() => Give(to: Giver));
    }

    [Fact]
    public void Somebody_you_share_nothing_with_cannot_be_thanked()
    {
        // The rule the whole eligibility list is built from: recognition means something because it comes from
        // people who saw the work.
        Should.Throw<DomainRuleViolationException>(() => Give(relation: KudoRelation.None));
    }

    [Fact]
    public void A_project_teammate_in_another_unit_may_be_thanked()
    {
        var kudo = Give(relation: KudoRelation.ProjectPeer);

        kudo.ShouldNotBeNull();
    }

    [Fact]
    public void A_head_may_thank_within_their_scope()
    {
        var kudo = Give(relation: KudoRelation.HeadScope);

        kudo.ShouldNotBeNull();
    }

    [Fact]
    public void A_reason_is_required()
    {
        // A category with nothing said about it is a click, and the annual claim view is made of the sentences.
        Should.Throw<DomainRuleViolationException>(() => Give(message: "   "));
    }

    [Fact]
    public void A_reason_has_a_ceiling()
    {
        Should.Throw<DomainRuleViolationException>(
            () => Give(message: new string('x', Kudo.MaximumMessageLength + 1)));
    }

    [Fact]
    public void A_category_the_department_does_not_offer_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => Give(category: "employee-of-the-month"));
    }

    [Fact]
    public void The_last_kudo_of_the_month_is_allowed()
    {
        var kudo = Give(givenThisMonth: 9, cap: 10);

        kudo.ShouldNotBeNull();
    }

    [Fact]
    public void The_one_after_the_cap_is_refused()
    {
        // The only anti-abuse rule here that refuses rather than flags. An eleventh kudo where ten is the agreed
        // limit is not a fact about anything; it is the limit not existing.
        Should.Throw<DomainRuleViolationException>(() => Give(givenThisMonth: 10, cap: 10));
    }

    [Fact]
    public void A_kudo_carries_its_price_even_where_the_department_only_counts()
    {
        var kudo = Give(rules: KudoRules.Resolve("""{"mode":"counter"}"""), category: "above-and-beyond");

        // The mode decides what anybody sees, not what is recorded. A department switching points on later would
        // otherwise find its leaderboard empty of everything that happened before the settings change.
        kudo.Points.ShouldBe(5);
    }

    [Fact]
    public void A_department_may_reprice_a_canonical_category()
    {
        var rules = KudoRules.Resolve("""{"categories":[{"code":"cleanup","points":7}]}""");

        Give(rules: rules, category: "cleanup").Points.ShouldBe(7);
    }

    [Fact]
    public void Giving_announces_the_badge_it_crosses()
    {
        var rules = KudoRules.Resolve("""{"mode":"points-badges-leaderboard"}""");

        // Nine points on record; a five-point kudo takes them past the ten-point rung.
        var kudo = Give(
            rules: rules,
            category: "above-and-beyond",
            tally: KudoTally.Empty.Plus("initiative", 3).Plus("initiative", 3).Plus("initiative", 3));

        kudo.DomainEvents.OfType<BadgeAwarded>()
            .Select(badge => badge.BadgeCode)
            .ShouldContain("helping-hand");
    }

    [Fact]
    public void A_badge_already_held_is_not_announced_again()
    {
        var rules = KudoRules.Resolve("""{"mode":"points-badges-leaderboard"}""");

        var tally = KudoTally.Empty
            .Plus("above-and-beyond", 5)
            .Plus("above-and-beyond", 5)
            .Plus("above-and-beyond", 5);

        var kudo = Give(rules: rules, category: "cleanup", tally: tally);

        // Fifteen points already: helping-hand was crossed some time ago and cornerstone is still out of reach.
        kudo.DomainEvents.OfType<BadgeAwarded>().ShouldBeEmpty();
    }

    [Fact]
    public void A_counting_department_announces_no_badges_at_all()
    {
        var kudo = Give(category: "above-and-beyond", tally: KudoTally.Empty.Plus("initiative", 9));

        // Default rules are counter mode. A badge is a score with a name on it.
        kudo.DomainEvents.OfType<BadgeAwarded>().ShouldBeEmpty();
        kudo.DomainEvents.OfType<KudoGiven>().ShouldHaveSingleItem();
    }

    private static Kudo Give(
        Guid? to = null,
        KudoRules? rules = null,
        string category = "initiative",
        string? message = "Stayed late to unblock the release.",
        KudoRelation relation = KudoRelation.UnitPeer,
        int givenThisMonth = 0,
        int cap = 10,
        KudoTally? tally = null) =>
        Kudo.Give(
            Giver,
            to ?? Receiver,
            Unit,
            Department,
            rules ?? KudoRules.Default,
            category,
            message,
            relation,
            givenThisMonth,
            cap,
            tally ?? KudoTally.Empty,
            Now);
}
