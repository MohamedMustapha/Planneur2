using Cracra.Modules.Kudos.Domain;

namespace Cracra.Tests.Unit.Kudos;

/// <summary>
/// Badges, derived from the record rather than stored as awards.
/// </summary>
/// <remarks>
/// The property these tests are really about is that a badge is a function of the kudos and the current rules —
/// which is what makes a department raising a threshold correct everywhere at once rather than leaving half a unit
/// holding a badge nobody can explain.
/// </remarks>
public sealed class BadgeLadderTests
{
    private static readonly DateTimeOffset January = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static readonly KudoRules Scoring = KudoRules.Resolve("""{"mode":"points-badges-leaderboard"}""");

    [Fact]
    public void The_first_kudo_earns_the_first_badge()
    {
        var badges = BadgeLadder.Earned(Scoring, [Kudo("cleanup", 1, January)]);

        badges.Select(badge => badge.Code).ShouldBe(["first-kudo"]);
    }

    [Fact]
    public void A_badge_is_dated_by_the_kudo_that_crossed_it()
    {
        var crossing = January.AddDays(30);

        var badges = BadgeLadder.Earned(Scoring,
        [
            Kudo("above-and-beyond", 5, January),
            Kudo("above-and-beyond", 5, crossing),
        ]);

        badges.Single(badge => badge.Code == "helping-hand").EarnedAt.ShouldBe(crossing);
    }

    [Fact]
    public void Order_of_the_input_does_not_change_the_dates()
    {
        var later = January.AddDays(30);

        // The wall reads newest-first, which is the order most callers have to hand. Assuming chronological would
        // date every badge to somebody's first kudo.
        var badges = BadgeLadder.Earned(Scoring,
        [
            Kudo("above-and-beyond", 5, later),
            Kudo("above-and-beyond", 5, January),
        ]);

        badges.Single(badge => badge.Code == "helping-hand").EarnedAt.ShouldBe(later);
    }

    [Fact]
    public void A_category_badge_counts_only_its_own_category()
    {
        var mentoring = Enumerable.Range(0, 5)
            .Select(day => Kudo("mentoring", 3, January.AddDays(day)))
            .ToList();

        BadgeLadder.Earned(Scoring, mentoring).Select(badge => badge.Code).ShouldContain("mentor");

        var mixed = Enumerable.Range(0, 5)
            .Select(day => Kudo("initiative", 3, January.AddDays(day)))
            .ToList();

        // Same count, same points, different category — recognition for mentoring is a distinct claim and the
        // ladder is what keeps it one.
        BadgeLadder.Earned(Scoring, mixed).Select(badge => badge.Code).ShouldNotContain("mentor");
    }

    [Fact]
    public void A_counting_department_has_no_badges()
    {
        var badges = BadgeLadder.Earned(KudoRules.Default, [Kudo("above-and-beyond", 5, January)]);

        badges.ShouldBeEmpty();
    }

    [Fact]
    public void Raising_a_threshold_takes_the_badge_back()
    {
        var record = new[] { Kudo("above-and-beyond", 5, January), Kudo("above-and-beyond", 5, January.AddDays(1)) };

        var lenient = KudoRules.Resolve(
            """{"mode":"points","badges":[{"code":"pillar","minPoints":10}]}""");
        var strict = KudoRules.Resolve(
            """{"mode":"points","badges":[{"code":"pillar","minPoints":50}]}""");

        // This is the whole reason badges are derived. Under a stored award, these ten points would keep a badge
        // the department has since said is worth fifty — sitting next to a colleague with more points and none.
        BadgeLadder.Earned(lenient, record).ShouldHaveSingleItem();
        BadgeLadder.Earned(strict, record).ShouldBeEmpty();
    }

    [Fact]
    public void A_tally_accumulates_by_category_and_in_total()
    {
        var tally = KudoTally.Empty.Plus("cleanup", 1).Plus("mentoring", 3).Plus("cleanup", 1);

        tally.Count.ShouldBe(3);
        tally.Points.ShouldBe(5);
        tally.CountIn("cleanup").ShouldBe(2);
        tally.PointsIn("mentoring").ShouldBe(3);
        tally.CountIn(null).ShouldBe(3);
    }

    [Fact]
    public void Newly_earned_names_only_what_this_kudo_crossed()
    {
        var before = KudoTally.Empty.Plus("initiative", 3).Plus("initiative", 3).Plus("initiative", 3);
        var after = before.Plus("above-and-beyond", 5);

        var crossed = BadgeLadder.NewlyEarned(Scoring, before, after);

        // first-kudo was crossed three kudos ago; nobody wants to be told about it again on every one since.
        crossed.Select(badge => badge.Code).ShouldBe(["helping-hand"]);
    }

    private static KudoRecord Kudo(string category, int points, DateTimeOffset at) =>
        new(category, points, at);
}
