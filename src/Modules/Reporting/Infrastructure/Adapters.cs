using System.Runtime.CompilerServices;
using Cracra.BuildingBlocks.Ai;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Kudos.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Reporting.Application;
using Cracra.Modules.Reporting.Domain;
using Cracra.Modules.Scheduling.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Reporting.Infrastructure;

// =================================================================================================================
// One adapter per port, each a thin pass-through to another module's contract.
//
// They look like ceremony and are not: they are the seam that lets the composer be written against six ports it
// controls rather than against six other modules' evolving DTOs. Every one of them is caller-scoped, so a report
// composed for a member cannot pick up a row the member may not read.
// =================================================================================================================

internal sealed class ActivityAdapter(IActivityScheduler activities, IDepartmentConfigReader configs)
    : IActivityQueries
{
    /// <summary>The statutory week. Used when a department has said nothing, exactly as S5 does.</summary>
    private const decimal DefaultTargetHours = 35m;

    public async Task<IReadOnlyList<ActivityEntryView>> ForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        personIds.Count == 0 ? [] : await activities.GetForPeopleAsync(personIds, from, to, ct);

    public async Task<IReadOnlyList<NodeHoursSlice>> HoursByNodeAsync(
        Guid rootNodeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await activities.GetHoursByNodeAsync(rootNodeId, from, to, ct);

    public async Task<IReadOnlyList<ActivityEntryView>> ForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await activities.GetForProjectAsync(projectId, from, to, ct);

    public async Task<(decimal TargetHours, bool Enforced)> TargetAsync(Guid? departmentId, CancellationToken ct)
    {
        if (departmentId is not { } department || department == Guid.Empty)
        {
            return (DefaultTargetHours, false);
        }

        // Degrades rather than fails, as the contract asks consumers to: a department's knobs are a refinement of
        // platform behaviour, and not being able to read them should make the report slightly less specific, not
        // stop it rendering.
        var config = await configs.TryGetAsync(department, ct);

        return config is null
            ? (DefaultTargetHours, false)
            : (config.WeeklyTargetHours, config.EnforceWeeklyTarget);
    }
}

internal sealed class DirectoryAdapter(IDirectoryReader directory, INodeProfileReader profiles)
    : IDirectoryQueries
{
    public async Task<PersonSummary?> PersonAsync(Guid personId, CancellationToken ct) =>
        await directory.GetPersonAsync(personId, ct);

    public async Task<IReadOnlyList<PersonSummary>> PeopleAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct) =>
        await directory.GetPeopleAsync(unitId, departmentId, ct);

    public async Task<IReadOnlyList<UnitSummary>> UnitsAsync(Guid? departmentId, CancellationToken ct) =>
        await directory.GetUnitsAsync(departmentId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> DepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct) =>
        departmentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await directory.GetDepartmentNameKeysAsync(departmentIds, ct);

    public async Task<NodeProfileSnapshot?> NodeProfileAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct) =>
        unitId is { } unit
            ? await profiles.ResolveForUnitAsync(unit, ct)
            : departmentId is { } department
                ? await profiles.ResolveForDepartmentAsync(department, ct)
                : null;
}

internal sealed class ProjectAdapter(IProjectProvisioner projects, IProjectTeamReader teams) : IProjectQueries
{
    public async Task<ProjectSummary?> ProjectAsync(Guid projectId, CancellationToken ct)
    {
        var summaries = await projects.GetSummariesAsync([projectId], ct);

        return summaries.GetValueOrDefault(projectId);
    }

    public async Task<IReadOnlyList<ProjectSummary>> VisibleAsync(CancellationToken ct)
    {
        var ids = await projects.GetVisibleProjectIdsAsync(ct);

        if (ids.Count == 0)
        {
            return [];
        }

        var summaries = await projects.GetSummariesAsync(ids, ct);

        return [.. summaries.Values.OrderBy(project => project.Code, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<ProjectTeamMemberView>> TeamAsync(Guid projectId, CancellationToken ct) =>
        await teams.GetTeamAsync(projectId, ct);
}

internal sealed class PortfolioAdapter(IPortfolioIterationReader iterations, IPortfolioBoardReader board)
    : IPortfolioQueries
{
    public async Task<PortfolioBoard> BoardAsync(CancellationToken ct) =>
        await board.GetBoardAsync(null, ct);

    public async Task<IReadOnlyList<IterationSummary>> IterationsAsync(Guid projectId, CancellationToken ct) =>
        await iterations.GetForProjectAsync(projectId, ct);
}

internal sealed class MeetingAdapter(IMeetingCalendarReader meetings) : IMeetingQueries
{
    public async Task<IReadOnlyList<UpcomingEntry>> InWindowAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await meetings.GetInWindowAsync(from, to, ct);
}

internal sealed class ScheduleAdapter(IScheduleLoadReader schedule) : IScheduleQueries
{
    public async Task<ScheduleLoad> LoadAsync(Guid unitId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await schedule.GetLoadAsync(unitId, from, to, ct);
}

/// <summary>
/// The S9 seam, filled.
/// </summary>
/// <remarks>
/// This adapter is what the seam was for. The unit report has asked for a kudos count and rendered it since S8,
/// against a stub that returned zero; S9 changed one registration and the figure started being true. Nothing in
/// the report contract, the PDF renderer or the Angular view moved — which is exactly how S6's calendar seam
/// turned into one line when S7 arrived.
/// </remarks>
internal sealed class KudosAdapter(IKudosReader kudos) : IKudosQueries
{
    public async Task<int> CountAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await kudos.CountAsync(unitId, departmentId, from, to, ct);
}

/// <summary>
/// The on-prem model, behind the port the spec asks for.
/// </summary>
/// <remarks>
/// The whole of the adaptation is turning the module's own <see cref="SummaryRequest"/> into two chat messages.
/// That is deliberately all it is: anything cleverer here — retries that change the prompt, post-processing of
/// the text — would be model behaviour hidden outside the place that decides what the model is asked.
/// </remarks>
internal sealed class AiSummarizer(IChatCompletionClient client, IOptions<AiOptions> options) : IAiSummarizer
{
    public string Model => options.Value.Model;

    public async Task<string> WriteAsync(SummaryRequest request, string language, CancellationToken ct) =>
        await client.CompleteAsync(Messages(request), language, ct);

    public async IAsyncEnumerable<string> StreamAsync(
        SummaryRequest request,
        string language,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in client.StreamAsync(Messages(request), language, ct))
        {
            yield return chunk;
        }
    }

    private static IReadOnlyList<ChatMessage> Messages(SummaryRequest request) =>
        [ChatMessage.System(request.System), ChatMessage.User(request.User)];
}

internal sealed class SummaryStore(ReportingDbContext context, IUserContext user) : ISummaryStore
{
    public async Task<StoredSummary?> FindAsync(
        ReportDescriptor descriptor,
        string promptHash,
        CancellationToken ct)
    {
        var row = await Scoped(descriptor)
            .Where(summary => summary.PromptHash == promptHash)
            .OrderByDescending(summary => summary.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : Project(row);
    }

    public async Task<StoredSummary?> FindLatestAsync(ReportDescriptor descriptor, CancellationToken ct)
    {
        var row = await Scoped(descriptor)
            .OrderByDescending(summary => summary.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : Project(row);
    }

    public async Task<StoredSummary> SaveAsync(
        ReportDescriptor descriptor,
        string promptHash,
        string model,
        string text,
        int promptCharacters,
        CancellationToken ct)
    {
        var summary = new GeneratedSummary
        {
            Id = Guid.CreateVersion7(),
            Scope = descriptor.Scope,
            ScopeId = descriptor.ScopeId,
            OwnerPersonId = user.UserId,
            UnitId = user.UnitId,
            DepartmentId = user.DepartmentIds.Count > 0 ? user.DepartmentIds[0] : null,
            PeriodKind = descriptor.Period.Kind,
            PeriodFrom = descriptor.Period.From,
            PeriodTo = descriptor.Period.To,
            Language = descriptor.Language,
            Model = model,
            PromptHash = promptHash,
            Text = text,
            PromptCharacters = promptCharacters,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.Summaries.Add(summary);

        BuildingBlocks.Persistence.Outbox.OutboxWriter.Enqueue(
            context,
            new Contracts.SummaryGenerated(
                summary.Id,
                summary.Scope,
                summary.ScopeId,
                summary.Language,
                summary.Model,
                summary.PromptCharacters,
                summary.Text.Length));

        await context.SaveChangesAsync(ct);

        return Project(summary);
    }

    /// <summary>
    /// Narratives for this exact report, and only the caller's own.
    /// </summary>
    /// <remarks>
    /// The owner filter is not the security boundary — RLS is, and it says the same thing. It is here because a
    /// unit head and one of their members can both hold a "my" report for the same week, and without it the head
    /// would be served the member's narrative simply for having read access to it.
    /// </remarks>
    private IQueryable<GeneratedSummary> Scoped(ReportDescriptor descriptor) =>
        context.Summaries
            .Where(summary => summary.OwnerPersonId == user.UserId)
            .Where(summary => summary.Scope == descriptor.Scope)
            .Where(summary => summary.ScopeId == descriptor.ScopeId)
            .Where(summary => summary.PeriodFrom == descriptor.Period.From)
            .Where(summary => summary.PeriodTo == descriptor.Period.To)
            .Where(summary => summary.Language == descriptor.Language);

    private static StoredSummary Project(GeneratedSummary summary) => new(
        summary.Id,
        summary.Text,
        summary.Model,
        summary.Language,
        summary.PromptHash,
        summary.CreatedAt);
}

internal sealed class OrgNodeQueries(IOrgNodeReader nodes) : IOrgNodeQueries
{
    public async Task<IReadOnlyList<OrgNodeSummary>> SubtreeAsync(Guid nodeId, CancellationToken ct) =>
        await nodes.GetSubtreeAsync(nodeId, ct);
}
