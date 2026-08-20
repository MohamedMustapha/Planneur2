using System.IO.Compression;
using System.Text;
using Cracra.BuildingBlocks.Documents;

namespace Cracra.Tests.Unit.Finance;

/// <summary>
/// The workbook writer.
/// </summary>
/// <remarks>
/// Hand-written markup is only defensible if it is pinned, and the two properties worth pinning are the ones that
/// would fail silently: that the parts a reader needs are all present, and that a number lands as a number rather
/// than as text that reads correctly and sums to zero.
/// </remarks>
public sealed class SpreadsheetDocumentTests
{
    [Fact]
    public void A_workbook_carries_every_part_a_reader_expects()
    {
        using var archive = Open(Simple());

        var names = archive.Entries.Select(entry => entry.FullName).ToList();

        // Omitting any of these produces the "unreadable content" dialog rather than an unstyled sheet, which is
        // exactly the kind of failure that only shows up on somebody else's machine.
        names.ShouldContain("[Content_Types].xml");
        names.ShouldContain("_rels/.rels");
        names.ShouldContain("xl/workbook.xml");
        names.ShouldContain("xl/_rels/workbook.xml.rels");
        names.ShouldContain("xl/styles.xml");
        names.ShouldContain("xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void A_number_is_written_as_a_number_and_text_as_text()
    {
        var sheet = Read(Simple(), "xl/worksheets/sheet1.xml");

        // The whole reason a finance export is xlsx rather than CSV: the recipient selects the column and reads
        // the sum. A numeric cell has a bare <v>; a text cell is an inline string.
        sheet.ShouldContain("<v>1234.5</v>");
        sheet.ShouldContain("<is><t xml:space=\"preserve\">PRJ-1</t></is>");
    }

    [Fact]
    public void The_header_row_is_bold_and_the_body_is_not()
    {
        var sheet = Read(Simple(), "xl/worksheets/sheet1.xml");

        sheet.ShouldContain("""<c r="A1" s="1" t="inlineStr">""");
        sheet.ShouldContain("""<c r="A2" s="0" t="inlineStr">""");
    }

    [Fact]
    public void Cell_references_carry_on_past_the_twenty_sixth_column()
    {
        var headers = Enumerable.Range(0, 28).Select(index => $"h{index}").ToList();

        var document = new SpreadsheetDocument().Sheet("Wide", headers, []);

        var sheet = Read(document.ToBytes(), "xl/worksheets/sheet1.xml");

        sheet.ShouldContain("""r="Z1" """.TrimEnd());
        sheet.ShouldContain("""r="AA1" """.TrimEnd());
        sheet.ShouldContain("""r="AB1" """.TrimEnd());
    }

    [Fact]
    public void Markup_in_a_value_is_escaped_rather_than_emitted()
    {
        var document = new SpreadsheetDocument().Sheet(
            "Escapes",
            ["name"],
            [SpreadsheetDocument.Row.Of(SpreadsheetDocument.Cell.From("R&D <tools> \"x\""))]);

        var sheet = Read(document.ToBytes(), "xl/worksheets/sheet1.xml");

        // A project called "R&D" is not exotic, and an unescaped ampersand makes the whole file unopenable.
        sheet.ShouldContain("R&amp;D &lt;tools&gt; &quot;x&quot;");
    }

    [Fact]
    public void A_sheet_name_excel_would_refuse_is_fixed_up_rather_than_thrown_at()
    {
        var document = new SpreadsheetDocument().Sheet(
            "Capex/Opex: 2026 [définitif] — un titre beaucoup trop long pour un onglet",
            ["a"],
            []);

        var workbook = Read(document.ToBytes(), "xl/workbook.xml");

        // The sheet element's own name, not the whole part — the workbook markup is full of slashes that belong
        // to its namespaces.
        var name = workbook.Split("<sheet name=\"")[1].Split('"')[0];

        name.Length.ShouldBeLessThanOrEqualTo(31);
        name.ShouldNotContain("/");
        name.ShouldNotContain("[");
        name.ShouldNotContain("]");
        name.ShouldNotContain(":");
    }

    [Fact]
    public void Several_sheets_each_get_their_own_part_and_relationship()
    {
        var document = new SpreadsheetDocument()
            .Sheet("One", ["a"], [])
            .Sheet("Two", ["a"], [])
            .Sheet("Three", ["a"], []);

        using var archive = Open(document.ToBytes());

        archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal))
            .ShouldBe(3);

        var relationships = Read(document.ToBytes(), "xl/_rels/workbook.xml.rels");

        // Three sheets and the styles part, and the styles id must sit past the sheets' or one of them silently
        // resolves to the wrong target.
        relationships.ShouldContain("""Id="rId3" """.TrimEnd());
        relationships.ShouldContain("""Id="rId4" """.TrimEnd());
        relationships.ShouldContain("styles.xml");
    }

    [Fact]
    public void The_same_content_produces_the_same_bytes()
    {
        // Determinism is what lets an export be asserted on rather than merely checked for being non-empty, and it
        // is the property a ZIP quietly breaks by stamping the current time on every entry.
        Simple().ShouldBe(Simple());
    }

    [Fact]
    public void A_workbook_with_no_sheets_is_refused()
    {
        // Excel will not open one, so producing it would turn a caller's mistake into a download that fails on
        // somebody else's machine.
        Should.Throw<InvalidOperationException>(() => new SpreadsheetDocument().ToBytes());
    }

    private static byte[] Simple() => new SpreadsheetDocument()
        .Sheet(
            "Projets",
            ["code", "cost"],
            [
                SpreadsheetDocument.Row.Of(
                    SpreadsheetDocument.Cell.From("PRJ-1"),
                    SpreadsheetDocument.Cell.From(1234.5m)),
            ])
        .ToBytes();

    private static ZipArchive Open(byte[] bytes) => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static string Read(byte[] bytes, string part)
    {
        using var archive = Open(bytes);
        using var stream = archive.GetEntry(part)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
