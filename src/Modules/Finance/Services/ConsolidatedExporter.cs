using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Documents;
using Cracra.BuildingBlocks.Storage;
using Cracra.Modules.Finance.Domain;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Finance.Services;

public sealed record ConsolidatedExportView(
    string Format,
    string ContentType,
    string Key,
    Uri Url,
    DateTimeOffset ExpiresAt);

public interface IConsolidatedExporter
{
    Task<ConsolidatedExportView> ExportAsync(ConsolidatedRequest request, string? format, CancellationToken ct);
}

/// <summary>
/// The consolidated view, as a workbook (v2 §04.3).
/// </summary>
/// <remarks>
/// <para>
/// Recomposed from the same service the screen calls, on the caller's own RLS session — so the file cannot
/// disagree with the page it was taken from, and a link somebody forwards cannot produce numbers its recipient
/// was not entitled to.
/// </para>
/// <para>
/// The tree is flattened into one sheet with a depth column rather than nested or indented. A spreadsheet is
/// something people sort and pivot, and either of those destroys visual indentation while a depth column survives
/// it — the shape stays recoverable after the first click on a column header.
/// </para>
/// </remarks>
internal sealed class ConsolidatedExporter(
    IConsolidationService consolidation,
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> storageOptions) : IConsolidatedExporter
{
    public async Task<ConsolidatedExportView> ExportAsync(
        ConsolidatedRequest request,
        string? format,
        CancellationToken ct)
    {
        var wanted = (format ?? "xlsx").Trim().ToLowerInvariant();

        if (wanted != "xlsx")
        {
            throw new DomainRuleViolationException($"'{format}' is not an export format.");
        }

        var view = await consolidation.GetAsync(request, ct);
        var bytes = Render(view);

        // Keyed by node and year, so two people exporting the same branch's figures get one object and a
        // re-export after a correction overwrites it rather than leaving a stale file somebody later trusts.
        var key = $"finance/consolidated/{view.FiscalYear}/{view.Node.NodeId:N}-{view.Mode}.xlsx";

        using (var content = new MemoryStream(bytes, writable: false))
        {
            await storage.PutAsync(key, content, SpreadsheetDocument.ContentType, ct);
        }

        var lifetime = storageOptions.Value.DefaultPresignLifetime;
        var url = await storage.PresignGetAsync(key, lifetime, ct);

        return new ConsolidatedExportView(
            wanted,
            SpreadsheetDocument.ContentType,
            key,
            url,
            DateTimeOffset.UtcNow.Add(lifetime));
    }

    internal static byte[] Render(ConsolidatedView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var document = new SpreadsheetDocument();

        document.Sheet(
            "Synthese",
            ["metric", "value"],
            [
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("fiscalYear"),
                    SpreadsheetDocument.Cell.From((decimal)view.FiscalYear)),
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("mode"),
                    SpreadsheetDocument.Cell.From(view.Mode)),
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("node"),
                    SpreadsheetDocument.Cell.From(view.Node.Name)),
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("subtree.capex"),
                    SpreadsheetDocument.Cell.From(view.Node.Subtree.Capex)),
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("subtree.opex"),
                    SpreadsheetDocument.Cell.From(view.Node.Subtree.Opex)),
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("subtree.excluded"),
                    SpreadsheetDocument.Cell.From(view.Node.Subtree.Excluded)),
            ]);

        document.Sheet(
            "Noeuds",
            [
                "depth", "code", "name", "own.capex", "own.opex", "subtree.capex", "subtree.opex",
                "subtree.excluded", "planned", "variance",
            ],
            [.. Flatten(view.Node, 0)]);

        return document.ToBytes();
    }

    private static IEnumerable<SpreadsheetDocument.Row> Flatten(ConsolidatedNode node, int depth)
    {
        yield return SpreadsheetDocument.Row.Of(
            SpreadsheetDocument.Cell.From((decimal)depth),
            SpreadsheetDocument.Cell.From(node.Code),
            SpreadsheetDocument.Cell.From(node.Name),
            SpreadsheetDocument.Cell.From(node.Own.Capex),
            SpreadsheetDocument.Cell.From(node.Own.Opex),
            SpreadsheetDocument.Cell.From(node.Subtree.Capex),
            SpreadsheetDocument.Cell.From(node.Subtree.Opex),
            SpreadsheetDocument.Cell.From(node.Subtree.Excluded),

            // Blank rather than zero where nobody set an envelope, for the same reason the API returns null: a
            // zero in a spreadsheet column gets summed, and summing "no budget" into a total is a lie.
            node.PlannedAmount is { } planned
                ? SpreadsheetDocument.Cell.From(planned)
                : SpreadsheetDocument.Cell.From(string.Empty),
            node.Variance is { } variance
                ? SpreadsheetDocument.Cell.From(variance)
                : SpreadsheetDocument.Cell.From(string.Empty));

        foreach (var child in node.Children)
        {
            foreach (var row in Flatten(child, depth + 1))
            {
                yield return row;
            }
        }
    }
}
