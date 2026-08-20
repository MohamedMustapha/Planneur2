using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Integrations.Services;

/// <summary>
/// Reports on the connections rather than on the systems behind them.
/// </summary>
/// <remarks>
/// <para>
/// architecture.md §6 asks for a health check per integration, and the obvious reading — probe DevOps and
/// ServiceNow — is the wrong one. A readiness probe runs every few seconds; pointing it at somebody else's
/// production instance turns our liveness monitoring into load on their service, and makes this API's readiness
/// depend on a system it is deliberately decoupled from. The platform works perfectly well with a stale mirror;
/// it must not refuse traffic because a ticketing system is having an afternoon.
/// </para>
/// <para>
/// So the check reads what the last pull recorded. Degraded, never unhealthy: a failing connection is a real
/// operational fact worth surfacing on the probe, and it is not a reason to take the instance out of rotation.
/// The name of the failing connection is in the data so whoever is looking knows which one to open.
/// </para>
/// </remarks>
internal sealed class IntegrationsHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        // The system context, like every other read that has to see across departments. A probe has no session
        // and RLS would fail it closed — reporting "no connections configured" for an instance with a dozen.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var connections = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();

        var active = await connections.Connections
            .Where(connection => connection.Active)
            .Select(connection => new { connection.Name, connection.Provider, connection.LastSyncStatus })
            .ToListAsync(cancellationToken);

        if (active.Count == 0)
        {
            // Nothing connected is the ordinary state of a deployment that does not use the integrations, and it
            // is healthy. Reporting degraded here would train an operator to ignore this check.
            return HealthCheckResult.Healthy("No integration is configured.");
        }

        var failing = active
            .Where(connection => connection.LastSyncStatus == SyncStatuses.Failed)
            .ToList();

        var data = new Dictionary<string, object>
        {
            ["configured"] = active.Count,
            ["failing"] = failing.Count,
        };

        return failing.Count == 0
            ? HealthCheckResult.Healthy($"{active.Count} connection(s), all pulling.", data)
            : HealthCheckResult.Degraded(
                $"{failing.Count} of {active.Count} connection(s) failed their last pull: "
                + string.Join(", ", failing.Select(connection => $"{connection.Provider}/{connection.Name}")),
                data: data);
    }
}
