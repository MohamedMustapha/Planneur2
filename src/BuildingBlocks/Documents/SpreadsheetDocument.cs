using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Cracra.BuildingBlocks.Documents;

/// <summary>
/// A minimal XLSX writer: one or more sheets of headed rows, with numbers that arrive as numbers.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written for the same reason <see cref="PdfDocument"/> is, and the reasoning is worth repeating because
/// this one is more surprising. An xlsx is a ZIP of a handful of XML parts, and <c>System.IO.Compression</c> is
/// in the framework — so the whole of "export a table to Excel" is about a hundred and fifty lines of well-
/// specified markup. Against that, every mature spreadsheet library is either a licence decision somebody has to
/// own or a dependency an order of magnitude larger than the thing it is being used for.
/// </para>
/// <para>
/// What it gives up is real and worth stating plainly: no formulas, no styling beyond a bold header row, no
/// charts, no column widths, no dates as dates. What it keeps is the property that actually matters for a
/// finance export — numeric cells are written as numbers, so the recipient can sum a column without first
/// explaining to Excel that "1 234,50" was meant to be money.
/// </para>
/// <para>
/// Deterministic: the same content produces byte-identical output, because the ZIP entries are written with a
/// fixed timestamp. That is what lets an export be asserted on in a test rather than merely checked for being
/// non-empty.
/// </para>
/// </remarks>
public sealed class SpreadsheetDocument
{
    /// <summary>
    /// The epoch every entry is stamped with.
    /// </summary>
    /// <remarks>
    /// Fixed rather than "now", so two exports of the same figures are the same bytes. It is the ZIP format's own
    /// zero point — 1980-01-01 — which is the conventional choice for reproducible archives and is what most
    /// tooling shows for a file that declines to claim a modification time.
    /// </remarks>
    private static readonly DateTimeOffset ZipEpoch = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<Tab> _sheets = [];

    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Adds a sheet. The name is what appears on the tab; Excel refuses a few characters in one.</summary>
    public SpreadsheetDocument Sheet(string name, IReadOnlyList<string> headers, IReadOnlyList<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        _sheets.Add(new Tab(SafeName(name, _sheets.Count + 1), headers, rows));

        return this;
    }

    public byte[] ToBytes()
    {
        if (_sheets.Count == 0)
        {
            // A workbook with no sheets is not a file Excel will open, and producing one would turn a caller's
            // mistake into a download that fails on somebody else's machine.
            throw new InvalidOperationException("A spreadsheet needs at least one sheet.");
        }

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypesXml());
            Write(archive, "_rels/.rels", RootRelationshipsXml());
            Write(archive, "xl/workbook.xml", WorkbookXml());
            Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
            Write(archive, "xl/styles.xml", StylesXml());

            for (var index = 0; index < _sheets.Count; index++)
            {
                Write(archive, $"xl/worksheets/sheet{index + 1}.xml", SheetXml(_sheets[index]));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>One row. Cells are values, not strings — see <see cref="Cell"/> for why that matters.</summary>
    public sealed record Row(IReadOnlyList<Cell> Cells)
    {
        public static Row Of(params Cell[] cells) => new(cells);
    }

    /// <summary>
    /// One cell, either text or a number.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of exporting to a spreadsheet rather than to CSV. A number written as
    /// text lands in Excel as text, and the first thing the recipient does with a finance export is select a
    /// column and read the sum at the bottom — which reads zero.
    /// </remarks>
    public readonly record struct Cell
    {
        private Cell(string? text, decimal? number)
        {
            Text = text;
            Number = number;
        }

        public string? Text { get; }

        public decimal? Number { get; }

        public bool IsNumber => Number.HasValue;

        public static Cell From(string? text) => new(text ?? string.Empty, null);

        public static Cell From(decimal value) => new(null, value);

        public static Cell From(decimal? value) => value is { } number ? From(number) : From(string.Empty);
    }

    private sealed record Tab(string Name, IReadOnlyList<string> Headers, IReadOnlyList<Row> Rows);

    /// <summary>
    /// Trims a sheet name to what Excel will accept.
    /// </summary>
    /// <remarks>
    /// Excel refuses <c>: \ / ? * [ ]</c> and anything past 31 characters, and it refuses them by declining to
    /// open the file rather than by ignoring the name. A caller passing a project title is not doing anything
    /// unreasonable, so this fixes it up instead of throwing.
    /// </remarks>
    private static string SafeName(string? name, int ordinal)
    {
        var candidate = new string((name ?? string.Empty)
            .Where(character => !":\\/?*[]".Contains(character, StringComparison.Ordinal))
            .ToArray())
            .Trim();

        if (candidate.Length == 0)
        {
            return $"Sheet{ordinal}";
        }

        return candidate.Length <= 31 ? candidate : candidate[..31];
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        entry.LastWriteTime = ZipEpoch;

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.Write(content);
    }

    private string ContentTypesXml()
    {
        var overrides = new StringBuilder();

        for (var index = 0; index < _sheets.Count; index++)
        {
            overrides.Append(CultureInfo.InvariantCulture, $"""
                <Override PartName="/xl/worksheets/sheet{index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            {overrides}
            </Types>
            """;
    }

    private static string RootRelationshipsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private string WorkbookXml()
    {
        var sheets = new StringBuilder();

        for (var index = 0; index < _sheets.Count; index++)
        {
            sheets.Append(CultureInfo.InvariantCulture,
                $"""<sheet name="{Escape(_sheets[index].Name)}" sheetId="{index + 1}" r:id="rId{index + 1}"/>""");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <sheets>{sheets}</sheets>
            </workbook>
            """;
    }

    private string WorkbookRelationshipsXml()
    {
        var relationships = new StringBuilder();

        for (var index = 0; index < _sheets.Count; index++)
        {
            relationships.Append(CultureInfo.InvariantCulture, $"""
                <Relationship Id="rId{index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{index + 1}.xml"/>
                """);
        }

        // The styles part is a relationship like any other, and its id has to sit past the sheets'.
        relationships.Append(CultureInfo.InvariantCulture, $"""
            <Relationship Id="rId{_sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            """);

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            {relationships}
            </Relationships>
            """;
    }

    /// <summary>
    /// Two cell formats: plain, and bold for the header row.
    /// </summary>
    /// <remarks>
    /// The minimum a workbook must declare — Excel expects the fonts, fills, borders and cellXfs collections to
    /// exist even when there is nothing interesting in them, and omitting them produces the "unreadable content"
    /// dialog rather than an unstyled sheet.
    /// </remarks>
    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
        <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
        <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
        </styleSheet>
        """;

    private static string SheetXml(Tab sheet)
    {
        var rows = new StringBuilder();
        var rowNumber = 1;

        rows.Append(CultureInfo.InvariantCulture, $"""<row r="{rowNumber}">""");

        for (var column = 0; column < sheet.Headers.Count; column++)
        {
            rows.Append(TextCell(Reference(column, rowNumber), sheet.Headers[column], styleIndex: 1));
        }

        rows.Append("</row>");

        foreach (var row in sheet.Rows)
        {
            rowNumber++;
            rows.Append(CultureInfo.InvariantCulture, $"""<row r="{rowNumber}">""");

            for (var column = 0; column < row.Cells.Count; column++)
            {
                var cell = row.Cells[column];
                var reference = Reference(column, rowNumber);

                rows.Append(cell.IsNumber
                    ? NumberCell(reference, cell.Number!.Value)
                    : TextCell(reference, cell.Text ?? string.Empty, styleIndex: 0));
            }

            rows.Append("</row>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            <sheetData>{rows}</sheetData>
            </worksheet>
            """;
    }

    /// <summary>
    /// An inline string rather than an entry in the shared-strings table.
    /// </summary>
    /// <remarks>
    /// The shared table is a size optimization for workbooks that repeat the same text thousands of times. A
    /// capex/opex export repeats a handful of project codes, so the table would cost a whole extra part and an
    /// index to maintain in exchange for nothing measurable.
    /// </remarks>
    private static string TextCell(string reference, string value, int styleIndex) =>
        $"""<c r="{reference}" s="{styleIndex}" t="inlineStr"><is><t xml:space="preserve">{Escape(value)}</t></is></c>""";

    private static string NumberCell(string reference, decimal value) =>
        $"""<c r="{reference}"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>""";

    /// <summary>A1, B1, ... Z1, AA1. Excel tolerates a missing r attribute; every other reader does not.</summary>
    private static string Reference(int columnIndex, int rowNumber)
    {
        var name = string.Empty;
        var remaining = columnIndex;

        do
        {
            name = (char)('A' + (remaining % 26)) + name;
            remaining = (remaining / 26) - 1;
        }
        while (remaining >= 0);

        return name + rowNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
