using System.Globalization;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Portfolio.Contracts;

namespace Cracra.Modules.Guidance.Services;

public interface INextActionService
{
    Task<NextAction> ResolveAsync(CancellationToken ct);
}

internal sealed class NextActionService(
    IUserContext user,
    IShellNavigationService navigation,
    IObligationService obligations,
    IWeeklySummaryReader weeks,
    IIterationDeadlineReader iterations,
    TimeProvider clock) : INextActionService
{
    private const int IterationHorizonDays = 3;

    public async Task<NextAction> ResolveAsync(CancellationToken ct)
    {
        var shell = await navigation.ResolveAsync(ct);
        var signals = await obligations.SignalsAsync(ct);

        if (signals.OverdueActions > 0)
        {
            return Action("overdueActions", ShellSections.Meetings, Count(signals.OverdueActions));
        }

        if (signals.AwaitingTriage > 0)
        {
            return Action("triageProblems", ShellSections.Problems, Count(signals.AwaitingTriage));
        }

        var closing = await ClosingAsync(ct);

        if (closing is not null)
        {
            return Action(
                "closeIteration",
                ShellSections.Portfolio,
                new Dictionary<string, string>
                {
                    ["item"] = closing.ItemCode,
                    ["iteration"] = closing.IterationName,
                    ["date"] = closing.EndsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                });
        }

        var week = await weeks.GetCurrentAsync(ct);
        var remaining = week.TargetHours - week.ActualHours;

        if (remaining > 0)
        {
            return Action(
                "fillWeek",
                ShellSections.Week,
                new Dictionary<string, string> { ["hours"] = ObligationService.Format(remaining) });
        }

        return shell.Position switch
        {
            ShellPositions.Admin => Action("reviewOrg", ShellSections.Admin, None),
            ShellPositions.Pmo => Action("reviewObjectives", ShellSections.Strategy, None),
            ShellPositions.ProductOwner => Action("reviewItems", ShellSections.Portfolio, None),
            ShellPositions.Member => Action("weekComplete", ShellSections.Week, None),
            _ => Action("reviewNode", ShellSections.Node, None),
        };
    }

    private async Task<IterationDeadline?> ClosingAsync(CancellationToken ct)
    {
        if (!user.HasAnyRole(ContextualRole.ProductOwner, ContextualRole.ProjectLead, ContextualRole.Pmo))
        {
            return null;
        }

        var by = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(IterationHorizonDays);

        return (await iterations.GetClosingAsync(by, ct)).FirstOrDefault();
    }

    private static NextAction Action(string name, string section, IReadOnlyDictionary<string, string> parameters) =>
        new($"guidance.action.{name}", parameters, section, $"guidance.action.{name}.cta");

    private static readonly Dictionary<string, string> None = [];

    private static Dictionary<string, string> Count(int count) =>
        new() { ["count"] = count.ToString(CultureInfo.InvariantCulture) };
}
