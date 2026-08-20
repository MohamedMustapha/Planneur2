using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Tests.Integration.Integrations;

/// <summary>
/// Puts rows in S10's mirror, for tests of the modules that read it.
/// </summary>
/// <remarks>
/// <para>
/// S5's dropdown and S6a's pool used to be fed by sample sources that invented plausible tasks; S10 replaced
/// those with the mirror, so their tests need mirrored rows. This writes them directly rather than pulling them
/// through a provider, because those suites are about the dropdown and the pool — whether the pull that put the
/// rows there was idempotent is asserted where it belongs, next to the synchronizer.
/// </para>
/// <para>
/// Under the system context, which is the only context that may write these rows at all: the mirror's write
/// policy is <c>access.is_system()</c> and nothing else. A test seeding as a person would silently insert zero
/// rows and then assert about an empty dropdown, which is the sort of green tick that costs an afternoon.
/// </para>
/// </remarks>
internal static class ExternalMirrorSeed
{
    /// <summary>One mirrored item, in the terms a test cares about.</summary>
    internal sealed record Item(
        string ExternalId,
        string Title,
        Guid? ProjectId = null,
        Guid? UnitId = null,
        Guid? AssignedPersonId = null,
        string? Sprint = null,
        decimal? EstimatedHours = null);

    public static async Task SeedAsync(
        CracraApplicationFactory factory,
        string provider,
        Guid departmentId,
        params Item[] items)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
        var ct = TestContext.Current.CancellationToken;

        var now = DateTimeOffset.UtcNow;

        var connection = new ExternalConnection
        {
            Id = Guid.CreateVersion7(),
            DepartmentId = departmentId,
            Provider = provider,
            Name = $"Seeded {provider}",
            BaseUrl = "https://seeded.invalid",
            AuthRef = "seeded",
            ProjectOrQueue = provider == ExternalProviders.ServiceNow ? "Helpdesk N1" : "CRACRA",
            CurrentSprint = items.FirstOrDefault()?.Sprint,

            // Zero, so a scheduler tick can never pull a connection whose base URL points nowhere. These rows are
            // placed, not fetched.
            PollInterval = TimeSpan.Zero,
            CreatedAt = now,
            ModifiedAt = now,
        };

        context.Connections.Add(connection);

        foreach (var item in items)
        {
            context.WorkItems.Add(new ExternalWorkItem
            {
                Id = Guid.CreateVersion7(),
                ConnectionId = connection.Id,
                Provider = provider,
                ExternalId = item.ExternalId,
                Reference = Reference(provider, item.ExternalId),
                Title = item.Title,
                Type = provider == ExternalProviders.ServiceNow ? "incident" : "Task",
                State = "Active",
                AssignedPersonId = item.AssignedPersonId,
                SprintOrQueue = item.Sprint,
                IsCurrentSprint = item.Sprint is not null,
                ProjectId = item.ProjectId,
                UnitId = item.UnitId,
                DepartmentId = departmentId,
                EstimatedHours = item.EstimatedHours,
                UpdatedAtSource = now,
                SyncedAt = now,
                MirrorState = MirrorStates.Open,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static string Reference(string provider, string externalId) =>
        provider == ExternalProviders.ServiceNow ? $"INC{externalId}" : $"AB#{externalId}";
}
