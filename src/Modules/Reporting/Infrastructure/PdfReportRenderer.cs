using System.Globalization;
using Cracra.BuildingBlocks.Documents;
using Cracra.Modules.Reporting.Application;
using Cracra.Modules.Reporting.Contracts;

namespace Cracra.Modules.Reporting.Infrastructure;

/// <summary>
/// The report, as a PDF.
/// </summary>
/// <remarks>
/// <para>
/// Renders the same <see cref="ReportView"/> the screen renders, from the same composition — which is what makes
/// an export trustworthy. A separate query for the PDF would eventually answer a slightly different question than
/// the page it was exported from, and nobody would notice until two people compared printouts.
/// </para>
/// <para>
/// Keys are printed as keys. The server does not translate — it has no idea what language this reader's browser
/// is in, and conventions.md §5 puts localization on the client — so an exported PDF shows <c>reports.section.hours</c>
/// where the screen shows "Heures". That is a real limitation and the honest one: inventing a server-side
/// dictionary here would give the platform a second set of translations to keep in step with Transloco's.
/// </para>
/// </remarks>
internal sealed class PdfReportRenderer : IReportRenderer
{
    public string Format => "pdf";

    public string ContentType => "application/pdf";

    public byte[] Render(ReportView report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var document = new PdfDocument();

        document.Heading(report.ScopeLabel);
        document.Paragraph(
            $"{report.Scope} · {report.Period.From:yyyy-MM-dd} — {report.Period.To:yyyy-MM-dd} · {report.Language}");
        document.Rule();

        foreach (var section in report.Sections)
        {
            document.Subheading(section.TitleKey);

            if (section.Metrics.Count > 0)
            {
                document.Table(
                    ["metric", "value"],
                    [.. section.Metrics.Select(metric => (IReadOnlyList<string>)
                        [metric.Key, $"{Number(metric.Value)} {metric.Unit}"])]);
            }

            foreach (var table in section.Tables)
            {
                if (table.Rows.Count == 0)
                {
                    continue;
                }

                document.Paragraph(table.TitleKey, size: 9);
                document.Table(
                    [string.Empty, .. table.ColumnKeys],
                    [.. table.Rows.Select(row => (IReadOnlyList<string>)
                        [row.Label, .. row.Values.Select(Number)])]);
            }

            foreach (var note in section.Notes)
            {
                document.Paragraph(
                    note.Severity is { } severity
                        ? $"[{severity}] {note.Key}{Detail(note)}"
                        : $"{note.Key}{Detail(note)}",
                    size: 9);
            }
        }

        if (report.Summary is { } summary)
        {
            document.Rule();
            document.Subheading("reports.summary.title");

            // Labelled in the document itself, not only on the screen it came from. A PDF gets forwarded, and the
            // person who receives it has no other way to know a machine wrote this part.
            document.Paragraph(
                $"reports.summary.disclaimer ({summary.Model}, {summary.CreatedAt:yyyy-MM-dd HH:mm} UTC)",
                size: 8);

            document.Paragraph(summary.Text);
        }

        return document.ToBytes();
    }

    private static string Detail(ReportNote note) =>
        note.Text is { Length: > 0 } text ? $": {text}" : string.Empty;

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
