using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;

namespace Cracra.Tests.Integration.Integrations;

/// <summary>
/// An external system the tests can rewrite between pulls.
/// </summary>
/// <remarks>
/// <para>
/// Substituted for the real adapters, not for the HTTP client underneath them: each adapter's own DTO mapping is
/// covered in the unit suite against recorded payloads, and repeating that here would test the same code twice
/// while making these tests about JSON.
/// </para>
/// <para>
/// What these tests are about is what happens <em>after</em> a provider answers — the upsert, the closing pass,
/// the mapping resolution, and above all RLS — and every one of those needs the current set to change between
/// runs. Hence a mutable list rather than a fixed payload: "the source stopped returning this" is a fact only a
/// second, different answer can express.
/// </para>
/// </remarks>
internal sealed class FakeWorkItemProvider(string provider) : IExternalWorkItemProvider
{
    public string Provider => provider;

    public List<ExternalWorkItemSnapshot> Items { get; } = [];

    /// <summary>Set to make the next pull fail the way an unreachable instance does.</summary>
    public Exception? Fails { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchAsync(
        ProviderConnection connection,
        CancellationToken ct)
    {
        Calls++;

        return Fails is not null
            ? Task.FromException<IReadOnlyList<ExternalWorkItemSnapshot>>(Fails)
            : Task.FromResult<IReadOnlyList<ExternalWorkItemSnapshot>>([.. Items]);
    }

    public static ExternalWorkItemSnapshot DevOpsTask(
        string id,
        string title,
        string? assignedTo,
        string area = @"CRACRA\Platform",
        string sprint = "Sprint 42",
        decimal? estimate = 4m) =>
        new(
            id,
            $"AB#{id}",
            title,
            "Task",
            "Active",
            assignedTo,
            sprint,
            [new MappingHint(MappingKinds.AreaPath, area)],
            $"https://devops.intranet/CRACRA/_workitems/edit/{id}",
            estimate,
            new DateTimeOffset(2026, 8, 19, 14, 30, 0, TimeSpan.Zero));

    public static ExternalWorkItemSnapshot Incident(
        string id,
        string title,
        string? assignedTo = null,
        string group = "Helpdesk N1") =>
        new(
            id,
            $"INC{id}",
            title,
            "incident",
            "2",
            assignedTo,
            group,
            [new MappingHint(MappingKinds.AssignmentGroup, group)],
            $"https://cracra.service-now.com/nav_to.do?uri=task.do?sys_id={id}",
            null,
            new DateTimeOffset(2026, 8, 19, 6, 45, 0, TimeSpan.Zero));
}
