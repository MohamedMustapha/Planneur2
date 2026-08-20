using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Documents;
using Cracra.BuildingBlocks.Storage;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Finance.Services;

/// <summary>A rendered export, and where to fetch it.</summary>
public sealed record CapexOpexExportView(string Format, string ContentType, string Key, Uri Url, DateTimeOffset ExpiresAt);

public interface ICapexOpexExporter
{
    Task<CapexOpexExportView> ExportAsync(CapexOpexRequest request, string? format, CancellationToken ct);
}

/// <summary>
/// The capitalization view, as a workbook.
/// </summary>
/// <remarks>
/// <para>
/// Recomposed rather than rendered from something the screen sent up. The export runs the caller's own
/// authorization and their own RLS session, so a link somebody shares cannot produce numbers its recipient was
/// not entitled to — and the file cannot disagree with the screen it was taken from, because both come from one
/// query.
/// </para>
/// <para>
/// Three sheets, because the recipient of a capex/opex export does three things with it: reads the headline
/// split, reconciles it against the project list, and pivots the monthly effort. Keys are printed as keys, for
/// the reason the PDF renderer records — the server does not know the reader's language and inventing a
/// server-side dictionary would give the platform a second set of translations to keep in step with Transloco's.
/// </para>
/// </remarks>
internal sealed class CapexOpexExporter(
    ICapexOpexService capexOpex,
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> storageOptions) : ICapexOpexExporter
{
    public async Task<CapexOpexExportView> ExportAsync(
        CapexOpexRequest request,
        string? format,
        CancellationToken ct)
    {
        var wanted = (format ?? "xlsx").Trim().ToLowerInvariant();

        if (wanted != "xlsx")
        {
            throw new DomainRuleViolationException($"'{format}' is not an export format.");
        }

        var view = await capexOpex.GetAsync(request, ct);
        var bytes = Render(view);

        // Keyed by scope and period rather than by a request id: two people exporting the same department's
        // August figures should get the same object, and re-exporting after a correction should overwrite it
        // rather than leave a stale file that somebody later finds and trusts.
        var key = $"finance/{view.From:yyyy/MM}/capex-opex-{view.Scope}-{view.ScopeId?.ToString("N") ?? "all"}"
                  + $"-{view.From:yyyyMMdd}-{view.To:yyyyMMdd}.xlsx";

        using (var content = new MemoryStream(bytes, writable: false))
        {
            await storage.PutAsync(key, content, SpreadsheetDocument.ContentType, ct);
        }

        var lifetime = storageOptions.Value.DefaultPresignLifetime;
        var url = await storage.PresignGetAsync(key, lifetime, ct);

        return new CapexOpexExportView(
            wanted,
            SpreadsheetDocument.ContentType,
            key,
            url,
            DateTimeOffset.UtcNow.Add(lifetime));
    }

    internal static byte[] Render(CapexOpexView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var document = new SpreadsheetDocument();

        document.Sheet(
            "Synthese",
            ["metric", "value", "unit"],
            [
                Row("scope", view.Scope),
                Row("period", $"{view.From:yyyy-MM-dd} — {view.To:yyyy-MM-dd}"),
                Row("currency", view.Currency),
                Number("capex.amount", view.Totals.CapexAmount, view.Currency),
                Number("opex.amount", view.Totals.OpexAmount, view.Currency),
                Number("capex.manual", view.Totals.ManualCapex, view.Currency),
                Number("opex.manual", view.Totals.ManualOpex, view.Currency),
                Number("capex.effort", view.Totals.EffortCapex, view.Currency),
                Number("opex.effort", view.Totals.EffortOpex, view.Currency),
                Number("unallocated.amount", view.Totals.UnallocatedAmount, view.Currency),
                Number("capex.hours", view.Totals.CapexHours, "h"),
                Number("opex.hours", view.Totals.OpexHours, "h"),
                Number("excluded.hours", view.Totals.ExcludedHours, "h"),
                Number("unclassified.hours", view.Totals.UnclassifiedHours, "h"),

                // Stated rather than implied. A reader who does not know the effort columns are unpriced will
                // read a small number as a small cost instead of as an absent rate card.
                Row("effort.valued", view.Totals.EffortValued ? "yes" : "no"),
            ]);

        document.Sheet(
            "Projets",
            ["code", "name", "classification", "manual.cost", "currency", "counted", "build.hours", "run.hours",
             "effort.cost", "capex", "opex"],
            [
                .. view.Projects.Select(line => new SpreadsheetDocument.Row(
                [
                    SpreadsheetDocument.Cell.From(line.Code),
                    SpreadsheetDocument.Cell.From(line.Name),
                    SpreadsheetDocument.Cell.From(line.Classification),
                    SpreadsheetDocument.Cell.From(line.ManualCost),
                    SpreadsheetDocument.Cell.From(line.CostCurrency),
                    SpreadsheetDocument.Cell.From(line.CostCounted ? "yes" : "no"),
                    SpreadsheetDocument.Cell.From(line.BuildHours),
                    SpreadsheetDocument.Cell.From(line.RunHours),
                    SpreadsheetDocument.Cell.From(line.EffortCost),
                    SpreadsheetDocument.Cell.From(line.CapexAmount),
                    SpreadsheetDocument.Cell.From(line.OpexAmount),
                ])),
            ]);

        document.Sheet(
            "Mois",
            ["month", "capex.hours", "opex.hours", "excluded.hours", "capex.cost", "opex.cost"],
            [
                .. view.Periods.Select(line => new SpreadsheetDocument.Row(
                [
                    SpreadsheetDocument.Cell.From(line.Month),
                    SpreadsheetDocument.Cell.From(line.CapexHours),
                    SpreadsheetDocument.Cell.From(line.OpexHours),
                    SpreadsheetDocument.Cell.From(line.ExcludedHours),
                    SpreadsheetDocument.Cell.From(line.CapexCost),
                    SpreadsheetDocument.Cell.From(line.OpexCost),
                ])),
            ]);

        return document.ToBytes();
    }

    private static SpreadsheetDocument.Row Row(string metric, string value) =>
        SpreadsheetDocument.Row.Of(
            SpreadsheetDocument.Cell.From(metric),
            SpreadsheetDocument.Cell.From(value),
            SpreadsheetDocument.Cell.From(string.Empty));

    private static SpreadsheetDocument.Row Number(string metric, decimal value, string unit) =>
        SpreadsheetDocument.Row.Of(
            SpreadsheetDocument.Cell.From(metric),
            SpreadsheetDocument.Cell.From(Math.Round(value, 2, MidpointRounding.ToEven)),
            SpreadsheetDocument.Cell.From(unit));
}
