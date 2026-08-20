using System.Text;
using Cracra.BuildingBlocks.Documents;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// The hand-written PDF writer.
/// </summary>
/// <remarks>
/// Worth testing because it is hand-written: a library would come with its own suite, and this one's failure mode
/// is a file that downloads successfully and will not open. The assertions are therefore structural — the header,
/// the trailer, the cross-reference table — rather than about how it looks.
/// </remarks>
public sealed class PdfDocumentTests
{
    [Fact]
    public void A_document_starts_with_the_version_header_and_ends_with_the_marker()
    {
        var bytes = new PdfDocument().Heading("Rapport").Paragraph("Une phrase.").ToBytes();
        var text = Encoding.Latin1.GetString(bytes);

        text.ShouldStartWith("%PDF-1.7");
        text.TrimEnd().ShouldEndWith("%%EOF");
    }

    [Fact]
    public void The_cross_reference_offset_points_at_the_cross_reference_table()
    {
        // The one structural detail a reader will refuse the file over, and the one most easily broken by
        // appending anything to the body.
        var text = Encoding.Latin1.GetString(new PdfDocument().Heading("A").ToBytes());

        var marker = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var offset = int.Parse(
            text[(marker + "startxref".Length)..].Trim().Split('\n')[0].Trim(),
            System.Globalization.CultureInfo.InvariantCulture);

        text[offset..].ShouldStartWith("xref");
    }

    [Fact]
    public void Parentheses_in_content_are_escaped()
    {
        // Unescaped, they close the PDF string literal early and every reader rejects what follows. A project
        // called "Migration (phase 2)" is not exotic.
        var text = Encoding.Latin1.GetString(
            new PdfDocument().Paragraph("Migration (phase 2)").ToBytes());

        text.ShouldContain("Migration \\(phase 2\\)");
    }

    [Fact]
    public void Accented_characters_survive()
    {
        // French and Spanish are two of the three languages this platform ships in, so "déphasé" has to render.
        var text = Encoding.Latin1.GetString(new PdfDocument().Paragraph("déphasé").ToBytes());

        text.ShouldContain("déphasé");
    }

    [Fact]
    public void Long_content_spills_onto_a_second_page()
    {
        var document = new PdfDocument();

        for (var line = 0; line < 200; line++)
        {
            document.Paragraph($"Ligne {line}");
        }

        var text = Encoding.Latin1.GetString(document.ToBytes());

        // Silently writing past the bottom of page one is the failure this catches: nothing errors, the file
        // opens, and a third of the report is invisible.
        var count = int.Parse(
            text.Split("/Count ")[1].Split(' ')[0].TrimEnd('>'),
            System.Globalization.CultureInfo.InvariantCulture);

        count.ShouldBeGreaterThan(1);
        text.ShouldContain("/Type /Page ");
    }

    [Fact]
    public void The_same_content_produces_the_same_bytes()
    {
        // Deterministic on purpose: an export can then be diffed in a test rather than merely checked for being
        // non-empty, and re-exporting an unchanged report does not churn object storage.
        new PdfDocument().Heading("A").Table(["x", "y"], [["1", "2"]]).ToBytes()
            .ShouldBe(new PdfDocument().Heading("A").Table(["x", "y"], [["1", "2"]]).ToBytes());
    }

    [Fact]
    public void A_table_declares_both_fonts_it_uses()
    {
        var text = Encoding.Latin1.GetString(
            new PdfDocument().Table(["header"], [["row"]]).ToBytes());

        // Headers are bold and rows are not, so a page missing the bold font resource renders nothing at all
        // where the header should be.
        text.ShouldContain("/F1 3 0 R");
        text.ShouldContain("/F2 4 0 R");
    }
}
