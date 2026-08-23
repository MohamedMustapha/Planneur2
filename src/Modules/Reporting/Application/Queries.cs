using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Modules.Reporting.Application;

// =================================================================================================================
// The read side. Each of these resolves a descriptor and hands it to the composer; none of them contains a rule,
// because a rule living in a query is a rule the domain cannot see.
// =================================================================================================================

public sealed record GetReportQuery(
    string? Scope,
    Guid? ScopeId,
    string? Period,
    DateOnly? From,
    DateOnly? To,
    string? Language) : IRequest<ReportView>;

internal sealed class GetReportHandler(ReportRequestService reports)
    : IRequestHandler<GetReportQuery, ReportView>
{
    public async Task<ReportView> Handle(GetReportQuery request, CancellationToken ct) =>
        await reports.ComposeAsync(
            reports.Describe(request.Scope, request.ScopeId, request.Period, request.From, request.To, request.Language),
            ct);
}

public sealed record GetNodeBriefQuery(
    Guid NodeId,
    string? Period,
    DateOnly? From,
    DateOnly? To,
    string? Depth) : IRequest<NodeBriefView?>;

internal sealed class GetNodeBriefHandler(INodeBriefComposer briefs)
    : IRequestHandler<GetNodeBriefQuery, NodeBriefView?>
{
    public async Task<NodeBriefView?> Handle(GetNodeBriefQuery request, CancellationToken ct)
    {
        var depth = request.Depth is { Length: > 0 } asked ? asked.ToLowerInvariant() : BriefDepths.Direct;

        if (!BriefDepths.Supported.Contains(depth, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{request.Depth}' is not a supported depth.");
        }

        var period = ReportPeriod.Resolve(
            request.Period,
            request.From,
            request.To,
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        return await briefs.ComposeAsync(request.NodeId, period, depth, ct);
    }
}

/// <summary>
/// Turning a request into a descriptor, and a descriptor into a report.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the query handlers, the summary command and the exporter, because all three have to agree exactly on
/// what was asked for. The failure this prevents is subtle and would be very hard to see: an export that resolved
/// the period slightly differently from the screen would produce a PDF whose numbers do not match the page it was
/// exported from.
/// </para>
/// <para>
/// Scope authorization happens here, once. It is the one role check in the module and it decides which question
/// may be asked, never which rows answer it — see <see cref="ReportScope"/>.
/// </para>
/// </remarks>
internal sealed class ReportRequestService(ReportComposer composer, ISummaryStore summaries, IUserContext user)
{
    public ReportDescriptor Describe(
        string? scope,
        Guid? scopeId,
        string? period,
        DateOnly? from,
        DateOnly? to,
        string? language)
    {
        var resolved = ReportScope.Resolve(user, scope);

        return new ReportDescriptor(
            resolved,
            ReportScope.RequiresScopeId(resolved) ? scopeId : null,
            ReportPeriod.Resolve(period, from, to, DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime)),
            // The viewer's own language, not a query parameter's, unless one was given. A report that silently
            // switched language would read as a bug to the person holding it.
            SupportedLanguages.Normalize(language ?? user.Language));
    }

    /// <summary>
    /// Rebuilds a descriptor from a report id, re-checking the scope.
    /// </summary>
    /// <remarks>
    /// The re-check is what makes the id safe to hand out. It says only what was asked for, and reopening it runs
    /// the same authorization and the same RLS session as the original request — so somebody holding another
    /// person's id gets their own answer to that question, or a 403.
    /// </remarks>
    public ReportDescriptor Reopen(string id)
    {
        var decoded = ReportIdentity.Decode(id);

        return decoded with { Scope = ReportScope.Resolve(user, decoded.Scope) };
    }

    /// <summary>Composes the report and attaches whatever narrative is already cached for it.</summary>
    public async Task<ReportView> ComposeAsync(ReportDescriptor descriptor, CancellationToken ct)
    {
        var report = await composer.ComposeAsync(descriptor, ct);

        var latest = await summaries.FindLatestAsync(descriptor, ct);

        if (latest is null)
        {
            return report;
        }

        // Attached whatever its age, and flagged when the figures have moved since. Hiding a stale narrative
        // would leave the panel empty for no visible reason; showing it unflagged would let somebody quote
        // sentences about numbers that are no longer on the screen beside them.
        return report with
        {
            Summary = new ReportSummaryView(
                latest.Id,
                latest.Text,
                latest.Model,
                latest.Language,
                latest.CreatedAt,
                Stale: latest.PromptHash != HashOf(descriptor, report, latest.Model)),
        };
    }

    public static string HashOf(ReportDescriptor descriptor, ReportView report, string model) =>
        ReportIdentity.PromptHash(descriptor, model, SummaryPrompt.Project(report));
}
