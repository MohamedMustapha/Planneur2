using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// What a report is asking for: scope, target, period, language.
/// </summary>
/// <remarks>
/// Everything a report is, other than the rows it will contain. Two requests with the same descriptor produce the
/// same report for the same viewer, which is what makes both the id and the summary cache work.
/// </remarks>
public sealed record ReportDescriptor(string Scope, Guid? ScopeId, ReportPeriod Period, string Language)
{
    public override string ToString() =>
        $"{Scope}|{ScopeId?.ToString() ?? "-"}|{Period}|{Language}";
}

/// <summary>
/// The report id, and the cache key for its narrative.
/// </summary>
/// <remarks>
/// <para>
/// A report has an id but is not a stored row. The id is an encoding of the request — scope, target, period,
/// language — so <c>GET /api/reports/{id}/export</c> can re-compose the same report rather than read back a
/// snapshot somebody else wrote.
/// </para>
/// <para>
/// That is the whole reason for this design, and it is worth being explicit about the alternative. Persisting a
/// snapshot on <c>GET /api/reports</c> would make a read write a row, and the row would then need its own
/// visibility rules on top of the ones its contents already had — a second place for the matrix to be wrong. Here
/// there is nothing to leak: the id says only what was asked, and re-composing runs the same scope authorization
/// and the same RLS session as the original request. A forged id gets its holder exactly the report they were
/// already entitled to, or a 403.
/// </para>
/// <para>
/// Base64url over a compact descriptor rather than a hash, because it has to decode. A hash would need a lookup
/// table, which is the snapshot table again under a different name.
/// </para>
/// </remarks>
public static class ReportIdentity
{
    /// <summary>Long enough for any legitimate descriptor, short enough that a hostile one cannot allocate.</summary>
    private const int MaximumEncodedLength = 512;

    public static string Encode(ReportDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(descriptor.ToString()));
    }

    /// <summary>
    /// Decodes an id back into the request it describes.
    /// </summary>
    /// <remarks>
    /// Every failure path lands on the same refusal. An id is opaque to whoever holds it, so telling them which
    /// field was malformed helps nobody but somebody probing the format.
    /// </remarks>
    public static ReportDescriptor Decode(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaximumEncodedLength)
        {
            throw new ResourceNotFoundException("That report does not exist.");
        }

        string text;

        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(id));
        }
        catch (FormatException)
        {
            throw new ResourceNotFoundException("That report does not exist.");
        }

        var parts = text.Split('|');

        if (parts.Length != 4)
        {
            throw new ResourceNotFoundException("That report does not exist.");
        }

        var scopeId = parts[1] == "-" ? (Guid?)null : Guid.TryParse(parts[1], out var parsed) ? parsed : null;

        if (parts[1] != "-" && scopeId is null)
        {
            throw new ResourceNotFoundException("That report does not exist.");
        }

        var period = parts[2].Split(':');

        if (period.Length != 3
            || !DateOnly.TryParse(period[1], out var from)
            || !DateOnly.TryParse(period[2], out var to))
        {
            throw new ResourceNotFoundException("That report does not exist.");
        }

        try
        {
            // Rebuilt through the period's own constructors rather than forced into Custom, so a decoded week is
            // still a week: it keeps its ISO numbers and its label, and the exported PDF says "Semaine 34" exactly
            // as the screen did.
            var rebuilt = period[0] switch
            {
                Contracts.ReportPeriods.Week => ReportPeriod.Week(from),
                Contracts.ReportPeriods.Month => ReportPeriod.Month(from),
                _ => ReportPeriod.Custom(from, to),
            };

            return new ReportDescriptor(parts[0], scopeId, rebuilt, parts[3]);
        }
        catch (DomainRuleViolationException)
        {
            // A window the period rules refuse — reversed, or a year and a half long. Same refusal as any other
            // malformed id, for the same reason.
            throw new ResourceNotFoundException("That report does not exist.");
        }
    }

    /// <summary>
    /// The hash the summary cache keys on: the descriptor, the model, and the numbers the model was shown.
    /// </summary>
    /// <remarks>
    /// The projection is in the hash, not merely the descriptor. Asking for last week's report twice must reuse
    /// the narrative; asking after somebody logged four more hours must not, because the text would then describe
    /// numbers that are no longer on the screen beside it. That is what "stale" means on the view.
    /// </remarks>
    public static string PromptHash(ReportDescriptor descriptor, string model, string projection)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var payload = $"{descriptor}|{model}|{projection}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
