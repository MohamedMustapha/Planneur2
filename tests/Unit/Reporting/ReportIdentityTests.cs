using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// Report ids and cache keys — periods, round-tripping, and what a malformed id does.
/// </summary>
public sealed class ReportIdentityTests
{
    private static readonly DateOnly Wednesday = new(2026, 8, 19);

    // --- Periods ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_week_runs_Monday_to_Sunday()
    {
        var period = ReportPeriod.Week(Wednesday);

        period.From.ShouldBe(new DateOnly(2026, 8, 17));
        period.To.ShouldBe(new DateOnly(2026, 8, 23));
        period.IsoWeek.ShouldBe(34);
        period.Days.ShouldBe(7);
    }

    [Fact]
    public void A_January_day_can_belong_to_the_previous_ISO_year()
    {
        // 1 January 2027 is a Friday in ISO week 53 of 2026. Treating it as week 1 of 2027 would split one week's
        // work across two reports — which is exactly what ISO exists to prevent.
        var period = ReportPeriod.Week(new DateOnly(2027, 1, 1));

        period.IsoYear.ShouldBe(2026);
        period.IsoWeek.ShouldBe(53);
    }

    [Fact]
    public void A_month_runs_to_its_own_last_day()
    {
        ReportPeriod.Month(new DateOnly(2026, 2, 10)).To.ShouldBe(new DateOnly(2026, 2, 28));
        ReportPeriod.Month(new DateOnly(2028, 2, 10)).To.ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void A_reversed_custom_range_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() =>
            ReportPeriod.Custom(new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public void A_range_longer_than_a_year_is_refused()
    {
        // A report is a period, not an archive. Somebody asking for three years has mistyped, and composing it
        // would run six modules' queries over every row they can see.
        Should.Throw<DomainRuleViolationException>(() =>
            ReportPeriod.Custom(new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void An_unresolved_period_defaults_to_this_week()
    {
        var period = ReportPeriod.Resolve(null, null, null, Wednesday);

        period.Kind.ShouldBe(ReportPeriods.Week);
        period.From.ShouldBe(new DateOnly(2026, 8, 17));
    }

    [Fact]
    public void A_custom_period_without_dates_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() =>
            ReportPeriod.Resolve(ReportPeriods.Custom, null, null, Wednesday));
    }

    // --- Ids -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_id_round_trips_through_its_encoding()
    {
        var descriptor = new ReportDescriptor(
            ReportScopes.Node, null, ReportPeriod.Week(Wednesday), "fr");

        var decoded = ReportIdentity.Decode(ReportIdentity.Encode(descriptor));

        decoded.Scope.ShouldBe(ReportScopes.Node);
        decoded.Language.ShouldBe("fr");
        decoded.Period.From.ShouldBe(descriptor.Period.From);

        // A decoded week is still a week: it keeps its ISO numbers, so the exported PDF says "Semaine 34" exactly
        // as the screen did rather than falling back to a bare date range.
        decoded.Period.Kind.ShouldBe(ReportPeriods.Week);
        decoded.Period.IsoWeek.ShouldBe(34);
    }

    [Fact]
    public void An_item_id_survives_the_round_trip()
    {
        var itemId = Guid.CreateVersion7();

        var decoded = ReportIdentity.Decode(ReportIdentity.Encode(
            new ReportDescriptor(ReportScopes.Item, itemId, ReportPeriod.Month(Wednesday), "en")));

        decoded.ScopeId.ShouldBe(itemId);
        decoded.Period.Kind.ShouldBe(ReportPeriods.Month);
    }

    [Fact]
    public void The_same_request_always_produces_the_same_id()
    {
        // Which is what lets a client cache an export link and a test assert on one.
        ReportIdentity.Encode(new ReportDescriptor(ReportScopes.Me, null, ReportPeriod.Week(Wednesday), "fr"))
            .ShouldBe(ReportIdentity.Encode(
                new ReportDescriptor(ReportScopes.Me, null, ReportPeriod.Week(Wednesday.AddDays(1)), "fr")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("YWJj")]
    public void A_malformed_id_is_a_404_whatever_is_wrong_with_it(string id)
    {
        // One refusal for every failure path. An id is opaque to whoever holds it, so saying which field was
        // malformed helps nobody except somebody probing the format.
        Should.Throw<ResourceNotFoundException>(() => ReportIdentity.Decode(id));
    }

    [Fact]
    public void An_id_encoding_an_impossible_window_is_refused_rather_than_composed()
    {
        var hostile = Convert.ToBase64String("my|-|custom:2020-01-01:2026-01-01|fr"u8.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Should.Throw<ResourceNotFoundException>(() => ReportIdentity.Decode(hostile));
    }

    // --- Cache keys ------------------------------------------------------------------------------------------

    [Fact]
    public void The_prompt_hash_changes_when_the_figures_do()
    {
        var descriptor = new ReportDescriptor(ReportScopes.Me, null, ReportPeriod.Week(Wednesday), "fr");

        var before = ReportIdentity.PromptHash(descriptor, "local-model", "hours: 35");
        var after = ReportIdentity.PromptHash(descriptor, "local-model", "hours: 39");

        before.ShouldNotBe(after);
    }

    [Fact]
    public void The_prompt_hash_changes_when_the_model_does()
    {
        var descriptor = new ReportDescriptor(ReportScopes.Me, null, ReportPeriod.Week(Wednesday), "fr");

        // A different model writes a different narrative from the same figures, so serving the old one under the
        // new model's name would misattribute it.
        ReportIdentity.PromptHash(descriptor, "local-model", "hours: 35")
            .ShouldNotBe(ReportIdentity.PromptHash(descriptor, "other-model", "hours: 35"));
    }

    [Fact]
    public void The_prompt_hash_changes_with_the_language()
    {
        var week = ReportPeriod.Week(Wednesday);

        ReportIdentity.PromptHash(new ReportDescriptor(ReportScopes.Me, null, week, "fr"), "m", "x")
            .ShouldNotBe(ReportIdentity.PromptHash(new ReportDescriptor(ReportScopes.Me, null, week, "es"), "m", "x"));
    }
}
