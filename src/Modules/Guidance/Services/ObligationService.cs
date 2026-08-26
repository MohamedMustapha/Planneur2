using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Problems.Contracts;

namespace Cracra.Modules.Guidance.Services;

/// <summary>The three sources allowed to pierce Focus mode (v2 02.2).</summary>
public sealed record ObligationSignals(int OverdueActions, int AwaitingTriage, decimal Overtime);

public interface IObligationService
{
    Task<ObligationSignals> SignalsAsync(CancellationToken ct);

    Task<IReadOnlyList<Obligation>> ListAsync(CancellationToken ct);
}

internal sealed class ObligationService(
    IUserContext user,
    IActionItemReader actions,
    IProblemTriageReader problems,
    IWeeklySummaryReader weeks) : IObligationService
{
    public async Task<ObligationSignals> SignalsAsync(CancellationToken ct)
    {
        var open = await actions.GetOpenForCallerAsync(ct);
        var week = await weeks.GetCurrentAsync(ct);

        var triage = Triages(user)
            ? await problems.CountAwaitingTriageAsync(ct)
            : 0;

        return new ObligationSignals(open.Count(action => action.Overdue), triage, week.Overtime);
    }

    public async Task<IReadOnlyList<Obligation>> ListAsync(CancellationToken ct)
    {
        var signals = await SignalsAsync(ct);
        var obligations = new List<Obligation>();

        if (signals.OverdueActions > 0)
        {
            obligations.Add(new Obligation(
                "overdue-actions",
                "obligation.overdueActions",
                Count(signals.OverdueActions),
                "danger"));
        }

        if (signals.AwaitingTriage > 0)
        {
            obligations.Add(new Obligation(
                "awaiting-triage",
                "obligation.awaitingTriage",
                Count(signals.AwaitingTriage),
                "warning"));
        }

        if (signals.Overtime > 0)
        {
            obligations.Add(new Obligation(
                "over-target",
                "obligation.overTarget",
                new Dictionary<string, string> { ["hours"] = Format(signals.Overtime) },
                "warning"));
        }

        return obligations;
    }

    internal static bool Triages(IUserContext caller) =>
        caller.HasAnyRole(ContextualRole.NodeHead, ContextualRole.Pmo, ContextualRole.Admin);

    private static Dictionary<string, string> Count(int count) =>
        new() { ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    internal static string Format(decimal hours) =>
        hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
