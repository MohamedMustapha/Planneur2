using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Projects.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Activities.Infrastructure;

/// <summary>
/// How the dropdown behaves before S10 lands.
/// </summary>
/// <remarks>
/// <para>
/// The seam is real from S5 onward, and this is what sits in it until the adapters exist. Off by default, in which
/// case every source returns nothing and the dropdown is simply empty — which is also the correct behaviour in a
/// deployment that never connects the external systems at all.
/// </para>
/// <para>
/// <c>SeedSampleTasks</c> turns on the sample data the dev box and the E2E suite need in order to exercise the
/// pull-and-prefill flow end to end. It is not a fallback for a misconfigured integration: S10's real adapters
/// replace these registrations rather than sitting behind them, so a production instance with a broken DevOps
/// connection shows an empty dropdown rather than invented tickets.
/// </para>
/// </remarks>
public sealed class AssignableTaskOptions
{
    public const string SectionName = "Cracra:Activities:AssignableTasks";

    public bool SeedSampleTasks { get; set; }
}

/// <summary>
/// Returns nothing, for a source that is declared but not connected.
/// </summary>
/// <remarks>
/// Logs at debug rather than warning: an unconnected source is a deployment choice, not a fault, and a warning per
/// dropdown open would be noise in every instance that never intends to integrate.
/// </remarks>
internal sealed class UnconfiguredTaskSource(string source, ILogger<UnconfiguredTaskSource> logger)
    : IAssignableTaskSource
{
    public string Source => source;

    public Task<IReadOnlyList<AssignableTask>> GetAssignableAsync(Guid personId, CancellationToken ct)
    {
        logger.LogDebug("No adapter is configured for {Source}; returning no assignable tasks", source);

        return Task.FromResult<IReadOnlyList<AssignableTask>>([]);
    }
}

/// <summary>
/// Sample tasks for the dev box and the E2E suite.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the caller's own projects rather than invented from nothing, so what the dropdown offers is
/// plausible: a developer sees sprint items against projects they are actually on, and picking one pre-fills a
/// project they are allowed to book against. A hard-coded list would pre-fill projects the guardrail then rejects,
/// and the flow would look broken for the wrong reason.
/// </para>
/// <para>
/// Deterministic, with no clock and no randomness, so an E2E assertion on a title stays true across runs.
/// </para>
/// </remarks>
internal sealed class SampleTaskSource(
    string source,
    string suggestedTypeCode,
    string titlePrefix,
    IProjectCatalogue projects) : IAssignableTaskSource
{
    public string Source => source;

    public async Task<IReadOnlyList<AssignableTask>> GetAssignableAsync(Guid personId, CancellationToken ct)
    {
        var mine = await projects.GetMyProjectsAsync(personId, ct);

        return
        [
            .. mine.SelectMany((project, index) => new[]
            {
                new AssignableTask(
                    source,
                    $"{Prefix(source)}-{1000 + index * 2}",
                    $"{titlePrefix} — {project.Code}",
                    "Active",
                    project.Id,
                    suggestedTypeCode),
                new AssignableTask(
                    source,
                    $"{Prefix(source)}-{1001 + index * 2}",
                    $"{titlePrefix} (suite) — {project.Code}",
                    "New",
                    project.Id,
                    suggestedTypeCode),
            }),
        ];
    }

    private static string Prefix(string source) => source == "servicenow" ? "INC" : "AB";
}

/// <summary>The caller's own projects, which is all the sample source needs and all it should be given.</summary>
public interface IProjectCatalogue
{
    Task<IReadOnlyList<(Guid Id, string Code)>> GetMyProjectsAsync(Guid personId, CancellationToken ct);
}

internal sealed class ProjectCatalogue(IProjectMembershipReader members, IProjectProvisioner projects)
    : IProjectCatalogue
{
    public async Task<IReadOnlyList<(Guid Id, string Code)>> GetMyProjectsAsync(
        Guid personId,
        CancellationToken ct)
    {
        // Everything RLS lets the caller read, narrowed to what they are actually on. The membership check is what
        // keeps a head from being offered sprint tasks for every project in their department.
        var visible = await projects.GetVisibleProjectIdsAsync(ct);

        var mine = new List<Guid>();

        foreach (var projectId in visible)
        {
            if (await members.IsActiveMemberAsync(projectId, personId, ct))
            {
                mine.Add(projectId);
            }
        }

        var summaries = await projects.GetSummariesAsync(mine, ct);

        return [.. summaries.Values.OrderBy(summary => summary.Code, StringComparer.Ordinal)
            .Select(summary => (summary.Id, summary.Code))];
    }
}

/// <summary>Wires whichever sources this deployment has.</summary>
internal static class AssignableTaskRegistration
{
    public static IReadOnlyList<IAssignableTaskSource> Build(
        IOptions<AssignableTaskOptions> options,
        IProjectCatalogue projects,
        ILogger<UnconfiguredTaskSource> logger) =>
        options.Value.SeedSampleTasks
            ?
            [
                new SampleTaskSource("azure-devops", "project-build", "Sprint task", projects),
                new SampleTaskSource("servicenow", "project-run", "Incident", projects),
            ]
            :
            [
                new UnconfiguredTaskSource("azure-devops", logger),
                new UnconfiguredTaskSource("servicenow", logger),
            ];
}
