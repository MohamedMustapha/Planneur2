using Cracra.Modules.Finance.Domain;
using Cracra.Modules.Finance.Services;

namespace Cracra.Tests.Unit.Finance;

/// <summary>
/// The consolidated workbook (v2 §04.3).
/// </summary>
/// <remarks>
/// Rendering is tested apart from storage because the two fail differently: a broken workbook is a silent wrong
/// number in somebody's spreadsheet, and a broken upload is a 500 they see immediately. Only the first needs
/// pinning here.
/// </remarks>
public sealed class ConsolidatedExportTests
{
    private static readonly Guid Top = Guid.Parse("e3000000-0000-0000-0000-000000000001");
    private static readonly Guid Child = Guid.Parse("e3000000-0000-0000-0000-000000000002");

    [Fact]
    public void The_workbook_renders()
    {
        var bytes = ConsolidatedExporter.Render(View());

        bytes.ShouldNotBeEmpty();

        // A zip, which is what an xlsx is. Enough to catch a renderer that produced text or nothing.
        bytes[0].ShouldBe((byte)'P');
        bytes[1].ShouldBe((byte)'K');
    }

    [Fact]
    public void Rendering_survives_a_tree_with_no_costs_in_it()
    {
        // The consolidated page never starts empty, so the export has to cope with the case where it opened on a
        // branch nobody has spent anything under yet.
        var empty = new ConsolidatedView(
            2026,
            "both",
            new ConsolidatedNode(Top, null, 1, "TOP", "Top", CostSplit.Zero, CostSplit.Zero, CostSplit.Zero,
                CostSplit.Zero, null, null, []),
            Landed: true);

        Should.NotThrow(() => ConsolidatedExporter.Render(empty));
    }

    [Fact]
    public void A_null_view_is_refused_rather_than_rendered_as_an_empty_file()
    {
        // An empty workbook that downloads successfully is worse than an error: somebody reconciles against it.
        Should.Throw<ArgumentNullException>(() => ConsolidatedExporter.Render(null!));
    }

    private static ConsolidatedView View()
    {
        var child = new ConsolidatedNode(
            Child,
            Top,
            2,
            "CHILD",
            "Child",
            new CostSplit(100m, 50m, 0m),
            CostSplit.Zero,
            new CostSplit(100m, 50m, 0m),
            new CostSplit(100m, 50m, 0m),
            null,
            null,
            []);

        var top = new ConsolidatedNode(
            Top,
            null,
            1,
            "TOP",
            "Top",
            CostSplit.Zero,
            CostSplit.Zero,
            CostSplit.Zero,
            new CostSplit(100m, 50m, 0m),
            500m,
            350m,
            [child]);

        return new ConsolidatedView(2026, "both", top, Landed: false);
    }
}
