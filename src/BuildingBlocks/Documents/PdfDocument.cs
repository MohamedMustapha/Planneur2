using System.Globalization;
using System.Text;

namespace Cracra.BuildingBlocks.Documents;

/// <summary>
/// A minimal PDF writer: pages of text, headings, tables and rules.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a library, and the reasoning is worth recording because it is a trade rather than a
/// clear win. The exported report is a title, some figures and a paragraph — no images, no flowing layout, no
/// fonts beyond the ones every PDF reader already has built in. Against that, every mature .NET PDF library
/// carries a licence decision somebody has to own: the popular ones are AGPL-with-a-commercial-option or free
/// only below a revenue threshold, and neither is a choice to make quietly inside a slice.
/// </para>
/// <para>
/// So this writes the subset of PDF 1.7 those documents need, using the standard Helvetica faces, in about two
/// hundred lines that can be read in full. What it gives up is real: no wrapping engine beyond the crude one
/// below, no Unicode outside WinAnsi, no vector graphics. If any of those become necessary, the interface this
/// sits behind is one class wide and swapping in a library is a contained change.
/// </para>
/// <para>
/// Deterministic on purpose — the same content produces byte-identical output — so an export can be diffed in a
/// test rather than merely checked for being non-empty.
/// </para>
/// </remarks>
public sealed class PdfDocument
{
    private const double PageWidth = 595.28;   // A4, in points.
    private const double PageHeight = 841.89;
    private const double Margin = 56;
    private const double BottomMargin = 56;

    private readonly List<PdfPage> pages = [];
    private PdfPage current;
    private double cursor;

    public PdfDocument()
    {
        current = new PdfPage();
        pages.Add(current);
        cursor = PageHeight - Margin;
    }

    /// <summary>Available width for text and tables.</summary>
    public static double ContentWidth => PageWidth - (2 * Margin);

    public PdfDocument Heading(string text, double size = 18)
    {
        Space(size * 0.6);
        Write(text, size, bold: true);
        Space(size * 0.35);

        return this;
    }

    public PdfDocument Subheading(string text) => Heading(text, 13);

    public PdfDocument Paragraph(string text, double size = 10)
    {
        foreach (var line in Wrap(text, size, ContentWidth))
        {
            Write(line, size, bold: false);
        }

        Space(size * 0.5);

        return this;
    }

    public PdfDocument Rule()
    {
        Reserve(6);
        cursor -= 4;

        current.Operations.Append(CultureInfo.InvariantCulture,
            $"0.75 w 0.8 G {F(Margin)} {F(cursor)} m {F(PageWidth - Margin)} {F(cursor)} l S\n");

        cursor -= 6;

        return this;
    }

    /// <summary>
    /// A table with a right-aligned numeric tail.
    /// </summary>
    /// <remarks>
    /// The first column is the label and takes whatever width the others leave, because a person's name or a
    /// project title is the part that needs room and the figures are all four or five characters wide.
    /// </remarks>
    public PdfDocument Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        const double size = 9;
        const double numericColumn = 78;

        var labelWidth = ContentWidth - (numericColumn * Math.Max(0, headers.Count - 1));

        WriteRow(headers, size, bold: true, labelWidth, numericColumn);
        Rule();

        foreach (var row in rows)
        {
            WriteRow(row, size, bold: false, labelWidth, numericColumn);
        }

        Space(size);

        return this;
    }

    public PdfDocument Space(double points)
    {
        cursor -= points;

        return this;
    }

    /// <summary>Serializes the document. Byte-identical for identical content.</summary>
    public byte[] ToBytes()
    {
        var body = new StringBuilder();
        var offsets = new List<int>();

        void Object(int number, string content)
        {
            offsets.Add(body.Length);
            body.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{content}\nendobj\n");
        }

        // 1 catalog, 2 pages, 3 regular font, 4 bold font, then one content stream per page.
        var pageIds = Enumerable.Range(0, pages.Count).Select(index => 5 + (index * 2)).ToArray();

        body.Append("%PDF-1.7\n");

        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>");
        Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Object(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = pageIds[index];
            var streamId = pageId + 1;
            var content = pages[index].Operations.ToString();
            var bytes = Latin1.GetByteCount(content);

            Object(pageId,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] "
                + $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {streamId} 0 R >>");

            Object(streamId, $"<< /Length {bytes} >>\nstream\n{content}endstream");
        }

        var xref = body.Length;

        body.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            body.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        body.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return Latin1.GetBytes(body.ToString());
    }

    /// <summary>
    /// WinAnsi, which is what the standard-font encoding declared above actually is.
    /// </summary>
    /// <remarks>
    /// It covers French and Spanish — the accented characters that matter here — and drops anything outside
    /// Latin-1 to a question mark rather than producing a corrupt stream. A report is a business document in one
    /// of three European languages, so that is the right place to stop.
    /// </remarks>
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private void WriteRow(
        IReadOnlyList<string> cells,
        double size,
        bool bold,
        double labelWidth,
        double numericColumn)
    {
        Reserve(size * 1.4);

        var font = bold ? "F2" : "F1";

        current.Operations.Append("BT\n");

        for (var index = 0; index < cells.Count; index++)
        {
            var x = index == 0
                ? Margin
                // Right-aligned: figures only line up if their right edges do, and a column of left-aligned
                // numbers is a column nobody can compare down.
                : Margin + labelWidth + (numericColumn * index) - TextWidth(cells[index], size);

            var text = index == 0 ? Truncate(cells[0], size, labelWidth - 6) : cells[index];

            current.Operations.Append(CultureInfo.InvariantCulture,
                $"/{font} {F(size)} Tf 1 0 0 1 {F(x)} {F(cursor)} Tm ({Escape(text)}) Tj\n");
        }

        current.Operations.Append("ET\n");

        cursor -= size * 1.4;
    }

    private void Write(string text, double size, bool bold)
    {
        Reserve(size * 1.45);

        current.Operations.Append(CultureInfo.InvariantCulture,
            $"BT /{(bold ? "F2" : "F1")} {F(size)} Tf 1 0 0 1 {F(Margin)} {F(cursor)} Tm ({Escape(text)}) Tj ET\n");

        cursor -= size * 1.45;
    }

    /// <summary>Starts a new page when the next line would fall off this one.</summary>
    private void Reserve(double height)
    {
        if (cursor - height >= BottomMargin)
        {
            return;
        }

        current = new PdfPage();
        pages.Add(current);
        cursor = PageHeight - Margin;
    }

    /// <summary>
    /// Greedy word wrap against an approximate width.
    /// </summary>
    /// <remarks>
    /// Approximate because measuring Helvetica properly means shipping its metrics table, and the consequence of
    /// being a few percent out is a line that ends slightly early. For a narrative paragraph that is invisible;
    /// for anything where it would not be, this class is the wrong tool.
    /// </remarks>
    private static IEnumerable<string> Wrap(string text, double size, double width)
    {
        foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                yield return string.Empty;

                continue;
            }

            var line = new StringBuilder();

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";

                if (TextWidth(candidate, size) > width && line.Length > 0)
                {
                    yield return line.ToString();

                    line.Clear().Append(word);

                    continue;
                }

                line.Clear().Append(candidate);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }
    }

    /// <summary>Helvetica averages a little over half its point size per character. Close enough to wrap on.</summary>
    private static double TextWidth(string text, double size) => text.Length * size * 0.5;

    private static string Truncate(string text, double size, double width)
    {
        if (TextWidth(text, size) <= width)
        {
            return text;
        }

        var characters = Math.Max(1, (int)(width / (size * 0.5)) - 1);

        return string.Concat(text.AsSpan(0, Math.Min(text.Length, characters)), "…");
    }

    /// <summary>Backslash, parentheses. Everything else in a PDF string literal is safe as written.</summary>
    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed class PdfPage
    {
        public StringBuilder Operations { get; } = new();
    }
}
