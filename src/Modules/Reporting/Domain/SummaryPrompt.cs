using System.Globalization;
using System.Text;
using Cracra.Modules.Reporting.Contracts;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// Turns a composed report into the two messages the on-prem model is sent.
/// </summary>
/// <remarks>
/// <para>
/// The division of labour is the point of this class. Every number in a report is computed in code; the model is
/// shown those numbers and asked only to write the sentences around them. A model that added up hours would be a
/// model whose arithmetic somebody has to check, and the whole value of the report is that its figures are
/// trustworthy without checking.
/// </para>
/// <para>
/// It is also where PII minimization happens. The projection sent out is the report's own structure — section
/// keys, metric keys, table rows — with person names replaced by stable pseudonyms. The model does not need to
/// know that row three is Camille to observe that one person carried most of the RUN load, and a narrative that
/// names people is a narrative somebody will paste into an email.
/// </para>
/// <para>
/// Pure and static: the prompt is the most testable thing in the module and the thing most worth testing, because
/// its failures are silent. A prompt that forgot to state the language produces a fluent, confident, English
/// summary for a French reader.
/// </para>
/// </remarks>
public static class SummaryPrompt
{
    /// <summary>
    /// The instruction the model is given, once, in English.
    /// </summary>
    /// <remarks>
    /// English regardless of the reader's language, and the output language stated separately. Translating the
    /// instruction too would mean three prompts to keep in step, and any drift between them would show up as the
    /// Spanish summary being subtly worse than the French one for reasons nobody could see.
    /// </remarks>
    private const string Instruction = """
        You write short status narratives for an internal delivery-tracking platform.

        You are given the already-computed figures of one report. Every number in them is correct and final.

        Rules:
        - Never compute, restate or contradict a figure. Refer to them; do not recalculate them.
        - Never invent a fact that is not in the input. If a section is empty, say so plainly or omit it.
        - Highlight, in this order: risks and things due soon, projects that were archived (déphasé), and
          quality-of-life initiatives worth crediting.
        - People appear as pseudonyms. Use them as given; never guess a real name.
        - Three short paragraphs at most. No headings, no bullet lists, no closing pleasantries.
        - Write as a colleague reporting to a colleague, not as an assistant addressing a user.
        """;

    /// <summary>Builds the two messages for a report.</summary>
    /// <param name="audience">
    /// The viewer's widest role, so the narrative is pitched at them. A unit head wants to hear about their
    /// people; the PMO wants to hear about states and money.
    /// </param>
    /// <remarks>
    /// Returns a neutral pair rather than the AI client's own message type. The spec asks for the model to sit
    /// behind a port, and a Domain class that named <c>ChatMessage</c> would have put the client's vocabulary in
    /// the one layer that is supposed to be free of everything.
    /// </remarks>
    public static SummaryRequest Build(ReportView report, string audience, string language)
    {
        ArgumentNullException.ThrowIfNull(report);

        var projection = Project(report);

        return new SummaryRequest(
            Instruction,
            $"""
             Scope: {report.Scope}
             Audience role: {audience}
             Period: {report.Period.From:yyyy-MM-dd} to {report.Period.To:yyyy-MM-dd}
             Output language (ISO 639-1): {language}

             Figures:
             {projection}
             """,
            projection);
    }

    /// <summary>
    /// The report as compact text, with names pseudonymized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text rather than JSON, deliberately. A local model given JSON tends to answer with JSON, and every token
    /// spent on braces is a token not spent on the summary — which matters on an on-prem box sized for the
    /// building rather than for a data centre.
    /// </para>
    /// <para>
    /// This is also the string the cache hashes. Two reports whose figures are identical share a narrative even
    /// if they were requested days apart; one extra logged hour changes the hash and the text is rewritten.
    /// </para>
    /// </remarks>
    public static string Project(ReportView report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var pseudonyms = new Pseudonymizer();
        var text = new StringBuilder();

        foreach (var section in report.Sections)
        {
            text.Append("# ").AppendLine(section.Key);

            foreach (var metric in section.Metrics)
            {
                text.Append("- ")
                    .Append(metric.Key)
                    .Append(": ")
                    .Append(metric.Value.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .AppendLine(metric.Unit);
            }

            foreach (var table in section.Tables)
            {
                text.Append("## ").AppendLine(table.TitleKey);
                text.Append("columns: ").AppendLine(string.Join(", ", table.ColumnKeys));

                foreach (var row in table.Rows)
                {
                    text.Append("- ")
                        .Append(table.IdentifiesPeople ? pseudonyms.Mask(row.Label) : row.Label)
                        .Append(": ")
                        .AppendLine(string.Join(
                            ", ",
                            row.Values.Select(value => value.ToString("0.##", CultureInfo.InvariantCulture))));
                }
            }

            foreach (var note in section.Notes)
            {
                text.Append("! ")
                    .Append(note.Key)
                    .Append(note.Severity is { } severity ? $" ({severity})" : string.Empty)
                    // Notes are never about a person — the composer keeps them to projects, dates and states —
                    // so their text goes out as written. That is deliberate: "Migration M365 was archived" is
                    // exactly the sentence the narrative is supposed to be able to build on.
                    .Append(note.Text is { Length: > 0 } detail ? $": {detail}" : string.Empty)
                    .AppendLine();
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Replaces person names with stable pseudonyms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only reached for tables flagged <c>IdentifiesPeople</c>, so it does not have to guess what a label is. An
    /// earlier version guessed — anything without a dot was treated as a name — and it would have masked every
    /// project in the portfolio report, leaving the model unable to write the one sentence it is most useful for.
    /// Deciding at composition time, where the shape of the row is actually known, is the fix.
    /// </para>
    /// <para>
    /// Stable within one report, so the model can say "P1 carried most of the RUN load" and mean one person
    /// throughout. Not stable across reports, because a pseudonym that survived would be one somebody could
    /// resolve by lining two reports up side by side.
    /// </para>
    /// </remarks>
    private sealed class Pseudonymizer
    {
        private readonly Dictionary<string, string> assigned = new(StringComparer.Ordinal);

        public string Mask(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (!assigned.TryGetValue(label, out var pseudonym))
            {
                pseudonym = $"P{assigned.Count + 1}";
                assigned[label] = pseudonym;
            }

            return pseudonym;
        }
    }
}

/// <summary>
/// What the model is asked, in the module's own vocabulary.
/// </summary>
/// <param name="Projection">
/// The figures exactly as they were sent. Kept alongside so the cache can hash it without rebuilding the prompt,
/// and so a stored summary can be checked against the numbers it was actually shown.
/// </param>
public sealed record SummaryRequest(string System, string User, string Projection);
