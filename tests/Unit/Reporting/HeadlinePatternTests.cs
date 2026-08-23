using Cracra.Modules.Reporting.Domain;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// The profile's headline sentence (v2 §10.5).
/// </summary>
/// <remarks>
/// A template renderer fails silently, which is why it is worth testing at all: a token nobody supplied renders as
/// nothing, the sentence still reads as a sentence, and no test that only checked "a headline was produced" would
/// notice. So these pin what happens to the gaps as firmly as what happens to the values.
/// </remarks>
public sealed class HeadlinePatternTests
{
    [Fact]
    public void No_pattern_produces_no_headline()
    {
        // Null, not empty. A deployment that configured no sentence gets the report's own heading; an empty
        // string would render as a blank line above the first section and look like a rendering bug.
        HeadlinePattern.Render(null, Values()).ShouldBeNull();
        HeadlinePattern.Render("   ", Values()).ShouldBeNull();
    }

    [Fact]
    public void Fills_the_tokens_the_spec_names()
    {
        var rendered = HeadlinePattern.Render(
            "{people} pers · ~{hours} h · {c1} {c1_label} · {c2} {c2_label} · risques : {risks}",
            HeadlinePattern.Values(
                peopleCount: 6,
                hours: 187.4m,
                leadingSubtypes:
                [
                    ("incident", "activity.type.incident", 62.5m),
                    ("dev", "activity.type.dev", 40m),
                ],
                risks: 2));

        rendered.ShouldBe(
            "6 pers · ~187 h · 63 activity.type.incident · 40 activity.type.dev · risques : 2");
    }

    [Fact]
    public void A_branch_with_one_busy_subtype_dashes_the_second_slot()
    {
        var rendered = HeadlinePattern.Render(
            "{c1} {c1_label} · {c2} {c2_label}",
            HeadlinePattern.Values(1, 8m, [("watch", "activity.type.watch", 8m)], 0));

        rendered.ShouldBe($"8 activity.type.watch · {HeadlinePattern.Missing} {HeadlinePattern.Missing}");
    }

    [Fact]
    public void An_unknown_token_becomes_a_dash_rather_than_leaking_the_brace()
    {
        // Showing `{invented}` to every reader of the report punishes them for an administrator's typo; dropping
        // it produces a sentence with a hole that reads as though the figure were zero. A dash is the honest one.
        HeadlinePattern.Render("a {invented} b", Values()).ShouldBe($"a {HeadlinePattern.Missing} b");
    }

    [Fact]
    public void Token_matching_ignores_case()
    {
        HeadlinePattern.Render("{People}", HeadlinePattern.Values(4, 0m, [], 0)).ShouldBe("4");
    }

    [Fact]
    public void Hours_are_rounded_away_from_zero_so_a_half_hour_never_disappears()
    {
        HeadlinePattern.Render("{hours}", HeadlinePattern.Values(1, 7.5m, [], 0)).ShouldBe("8");
    }

    [Fact]
    public void Only_the_first_two_subtypes_are_bound()
    {
        // c1 and c2 by design: a headline is one line. A branch with nine subtypes gets its two busiest, and the
        // table underneath carries the rest.
        var values = HeadlinePattern.Values(
            1,
            0m,
            [
                ("a", "label.a", 3m),
                ("b", "label.b", 2m),
                ("c", "label.c", 1m),
            ],
            0);

        values.ShouldContainKey("c2_label");
        values.ShouldNotContainKey("c3");
    }

    private static IReadOnlyDictionary<string, string> Values() =>
        HeadlinePattern.Values(1, 0m, [], 0);
}
