using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// What leaves for the model, and what does not.
/// </summary>
/// <remarks>
/// These are the assertions that matter most in the slice, because every failure they catch is silent. A prompt
/// that forgot the language produces a fluent, confident English summary for a French reader; a prompt that
/// forgot to mask produces a narrative naming individuals, which nobody notices until it has been forwarded.
/// </remarks>
public sealed class SummaryPromptTests
{
    [Fact]
    public void The_output_language_is_stated_rather_than_inferred()
    {
        var prompt = SummaryPrompt.Build(Report(), "unit-head", "es");

        prompt.User.ShouldContain("Output language (ISO 639-1): es");
    }

    [Fact]
    public void The_audience_role_is_stated()
    {
        SummaryPrompt.Build(Report(), "pmo", "fr").User.ShouldContain("Audience role: pmo");
    }

    [Fact]
    public void The_model_is_told_not_to_do_the_arithmetic()
    {
        var prompt = SummaryPrompt.Build(Report(), "member", "fr");

        // The whole division of labour in one instruction. Numbers are computed in code; the model writes the
        // sentences around them.
        prompt.System.ShouldContain("do not recalculate");
        prompt.System.ShouldContain("Never invent a fact");
    }

    [Fact]
    public void People_are_replaced_by_stable_pseudonyms()
    {
        var projection = SummaryPrompt.Project(Report());

        projection.ShouldNotContain("Camille Villeneuve");
        projection.ShouldNotContain("Mehdi Sadaoui");
        projection.ShouldContain("P1");
        projection.ShouldContain("P2");
    }

    [Fact]
    public void The_same_person_keeps_one_pseudonym_throughout_a_report()
    {
        var report = Report() with
        {
            Sections =
            [
                new ReportSection(
                    "members",
                    "reports.section.members",
                    [],
                    [
                        new ReportTable("a", ["h"], [new ReportRow("1", "Camille Villeneuve", [1])], IdentifiesPeople: true),
                        new ReportTable("b", ["h"], [new ReportRow("1", "Camille Villeneuve", [2])], IdentifiesPeople: true),
                    ],
                    []),
            ],
        };

        var projection = SummaryPrompt.Project(report);

        // Two tables, one person, one pseudonym — otherwise the model would describe them as two colleagues.
        projection.Split("P1").Length.ShouldBe(3);
        projection.ShouldNotContain("P2");
    }

    [Fact]
    public void Project_names_are_not_masked()
    {
        var projection = SummaryPrompt.Project(Report());

        // Not PII, and precisely what the narrative is supposed to be able to say: "Migration M365 was archived"
        // is the sentence a portfolio summary exists to produce.
        projection.ShouldContain("Migration M365");
    }

    [Fact]
    public void Translation_keys_survive_because_they_are_the_models_vocabulary()
    {
        var projection = SummaryPrompt.Project(Report());

        // Table titles, column headers and bucket labels all go out as keys. The model does not translate them —
        // it groups by them, which is what lets it say "most of it was BUILD" without being told which is which.
        projection.ShouldContain("reports.table.hoursByType");
        projection.ShouldContain("reports.column.actualHours");
        projection.ShouldContain("activity.projectBuild");
    }

    [Fact]
    public void Notes_carry_their_severity_so_risks_can_be_ranked()
    {
        SummaryPrompt.Project(Report()).ShouldContain("reports.note.overTarget (warning)");
    }

    [Fact]
    public void The_projection_is_stable_for_stable_figures()
    {
        // The cache hashes this string. If it were not deterministic, every request would miss and the on-prem
        // box would generate a fresh narrative for a report nobody had changed.
        SummaryPrompt.Project(Report()).ShouldBe(SummaryPrompt.Project(Report()));
    }

    [Fact]
    public void One_more_logged_hour_changes_the_projection()
    {
        var moved = Report() with
        {
            Sections =
            [
                Report().Sections[0] with
                {
                    Metrics = [new ReportMetric("actualHours", 36m, "hours")],
                },
                .. Report().Sections.Skip(1),
            ],
        };

        // Which is what makes a stale narrative detectable rather than merely old.
        SummaryPrompt.Project(moved).ShouldNotBe(SummaryPrompt.Project(Report()));
    }

    private static ReportView Report() => new(
        "id",
        ReportScopes.Node,
        null,
        "Infrastructure",
        new ReportPeriodView("week", new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23), 2026, 34, "reports.period.week"),
        "fr",
        [
            new ReportSection(
                "hours",
                "reports.section.hours",
                [new ReportMetric("actualHours", 35m, "hours")],
                [new ReportTable(
                    "reports.table.hoursByType",
                    ["reports.column.actualHours"],
                    [new ReportRow("project-build", "activity.projectBuild", [21m])])],
                [new ReportNote("reports.note.overTarget", null, "warning")]),
            new ReportSection(
                "members",
                "reports.section.members",
                [],
                [new ReportTable(
                    "reports.table.memberActivity",
                    ["reports.column.actualHours"],
                    [
                        new ReportRow("a", "Camille Villeneuve", [21m]),
                        new ReportRow("b", "Mehdi Sadaoui", [14m]),
                    ],
                    IdentifiesPeople: true)],
                []),
            new ReportSection(
                "archived",
                "reports.section.archived",
                [],
                [],
                [new ReportNote("reports.note.archived", "Migration M365", "info")]),
        ],
        [ReportScopes.Node, ReportScopes.Me],
        null,
        DateTimeOffset.UnixEpoch);
}
