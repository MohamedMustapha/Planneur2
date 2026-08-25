using System.Globalization;
using System.Text;
using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Ai;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Meetings.Services;

/// <summary>
/// Offers a first draft of the CR's summary (v2 §07.2).
/// </summary>
/// <remarks>
/// Optional in the spec and optional in the product: it returns text and saves nothing. Whoever ran the meeting
/// reads the draft, edits it and presses save, which is the only reading under which a generated paragraph can
/// carry somebody's name at the bottom of a record other people act on.
/// </remarks>
public interface IMinutesDrafter
{
    Task<string> DraftAsync(Guid minutesId, CancellationToken ct);
}

internal sealed class MinutesDrafter(
    MeetingsDbContext context,
    IChatCompletionClient client,
    IUserContext user) : IMinutesDrafter
{
    public async Task<string> DraftAsync(Guid minutesId, CancellationToken ct)
    {
        var minutes = await context.Minutes
            .Include(row => row.Decisions)
            .Include(row => row.Actions)
            .SingleOrDefaultAsync(row => row.Id == minutesId, ct)
            ?? throw new ResourceNotFoundException("Those minutes do not exist.");

        var brief = Brief(minutes);

        return await client.CompleteAsync(
            [ChatMessage.System(System), ChatMessage.User(brief)],
            user.Language,
            ct);
    }

    /// <summary>
    /// The S8 rule, restated for this slice: every number is counted here, and the model only writes prose.
    /// </summary>
    /// <remarks>
    /// The brief is the agenda and the decisions and actions already recorded — nothing the author has not
    /// entered. A model asked to fill gaps fills them, and a CR is exactly the document where an invented decision
    /// is acted on by somebody who was not in the room.
    /// </remarks>
    private static string Brief(MeetingMinutes minutes)
    {
        var brief = new StringBuilder();

        brief.Append(CultureInfo.InvariantCulture, $"Réunion du {minutes.OccurredAt:yyyy-MM-dd}.");
        brief.Append(CultureInfo.InvariantCulture, $" Participants : {minutes.Attendees.Length}.");
        brief.AppendLine(CultureInfo.InvariantCulture, $" Absents : {minutes.Absentees.Length}.");

        if (!string.IsNullOrWhiteSpace(minutes.Agenda))
        {
            brief.AppendLine().AppendLine("Ordre du jour :").AppendLine(minutes.Agenda);
        }

        brief.AppendLine().AppendLine(
            CultureInfo.InvariantCulture,
            $"Décisions ({minutes.Decisions.Count}) :");

        foreach (var decision in minutes.Decisions)
        {
            brief.AppendLine(CultureInfo.InvariantCulture,
                $"- {decision.Text}{(string.IsNullOrWhiteSpace(decision.Rationale) ? string.Empty : $" (motif : {decision.Rationale})")}");
        }

        var open = minutes.Actions.Count(action => action.Status == ActionStatuses.Open);

        brief.AppendLine().AppendLine(
            CultureInfo.InvariantCulture,
            $"Actions ({minutes.Actions.Count}, dont {open} ouvertes) :");

        foreach (var action in minutes.Actions)
        {
            brief.AppendLine(CultureInfo.InvariantCulture,
                $"- {action.Title} — {action.Status}{(action.Due is { } due ? $", échéance {due:yyyy-MM-dd}" : string.Empty)}");
        }

        return brief.ToString();
    }

    private const string System =
        "Tu rédiges la synthèse d'un compte rendu de réunion. Trois phrases au plus. "
        + "N'invente aucune décision, aucune action, aucun chiffre : n'utilise que ce qui t'est donné. "
        + "Écris au passé, sans formule d'introduction et sans titre.";
}
