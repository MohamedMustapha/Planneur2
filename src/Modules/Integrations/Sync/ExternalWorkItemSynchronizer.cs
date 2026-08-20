using System.Diagnostics;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Integrations.Sync;

/// <summary>What one pull did. Returned by the on-demand endpoint and logged to SEQ.</summary>
public sealed record SyncResult(Guid ConnectionId, int Created, int Updated, int Closed, TimeSpan Duration)
{
    public static SyncResult Nothing(Guid connectionId) => new(connectionId, 0, 0, 0, TimeSpan.Zero);

    public int TotalChanges => Created + Updated + Closed;
}

public interface IExternalWorkItemSynchronizer
{
    /// <summary>Pulls one connection, whatever its schedule says. What the on-demand endpoint calls.</summary>
    Task<SyncResult> SynchronizeAsync(Guid connectionId, CancellationToken ct);

    /// <summary>Pulls every active connection whose own poll interval has come due.</summary>
    Task<IReadOnlyList<SyncResult>> SynchronizeDueAsync(CancellationToken ct);
}

/// <summary>
/// Reconciles the mirror against one external system.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by construction: every item is an upsert keyed on <c>(provider, external_id)</c>, and the closing
/// pass is derived from what this pull saw rather than from what changed. Running it twice in a row changes
/// nothing the first run did not, which is what makes it safe to have a schedule, a startup pass and a button
/// all pointing at it.
/// </para>
/// <para>
/// Runs under the system context, like every background job (architecture.md §4). It has to: it writes mirror
/// rows for departments no human session has scope over, and it resolves LDAP uids against the whole directory —
/// neither of which any caller's session could do, and neither of which any caller's session should confer.
/// </para>
/// <para>
/// One transaction per connection, not one per run. A DevOps instance timing out must not roll back the
/// ServiceNow pull that already succeeded, and the two have nothing to be consistent about with each other.
/// </para>
/// </remarks>
internal sealed class ExternalWorkItemSynchronizer(
    IServiceScopeFactory scopeFactory,
    ILogger<ExternalWorkItemSynchronizer> logger) : IExternalWorkItemSynchronizer
{
    public async Task<SyncResult> SynchronizeAsync(Guid connectionId, CancellationToken ct)
    {
        await using var scope = SystemScope();

        var context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();

        var connection = await LoadAsync(context, connection => connection.Id == connectionId, ct);

        if (connection.Count == 0)
        {
            logger.LogWarning("Sync was asked for connection {ConnectionId}, which does not exist", connectionId);

            return SyncResult.Nothing(connectionId);
        }

        return await PullAsync(scope, context, connection[0], ct);
    }

    public async Task<IReadOnlyList<SyncResult>> SynchronizeDueAsync(CancellationToken ct)
    {
        await using var scope = SystemScope();

        var context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Due-ness is decided in memory rather than in SQL. It is one predicate over a handful of configuration
        // rows, and expressing "last_synced_at + poll_interval <= now" as a translatable query buys nothing but
        // a shape the domain rule can drift away from.
        var due = (await LoadAsync(context, connection => connection.Active, ct))
            .Where(connection => connection.IsDue(now))
            .ToList();

        if (due.Count == 0)
        {
            return [];
        }

        var results = new List<SyncResult>(due.Count);

        foreach (var connection in due)
        {
            results.Add(await PullAsync(scope, context, connection, ct));
        }

        return results;
    }

    private async Task<SyncResult> PullAsync(
        AsyncServiceScope scope,
        IntegrationsDbContext context,
        ExternalConnection connection,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await ReconcileAsync(scope, context, connection, ct);

            IntegrationTelemetry.RecordSuccess(
                connection.Id,
                connection.Provider,
                Stopwatch.GetElapsedTime(started),
                result.TotalChanges);

            logger.LogInformation(
                "Pulled {Provider} connection {ConnectionId} in {ElapsedMilliseconds:0} ms: "
                + "+{Created}, ~{Updated}, closed {Closed}",
                connection.Provider,
                connection.Id,
                result.Duration.TotalMilliseconds,
                result.Created,
                result.Updated,
                result.Closed);

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IntegrationTelemetry.RecordFailure(connection.Id, connection.Provider, Stopwatch.GetElapsedTime(started));

            logger.LogError(
                exception,
                "Pulling {Provider} connection {ConnectionId} failed; the mirror keeps what it already had",
                connection.Provider,
                connection.Id);

            await RecordFailureAsync(context, connection, exception, ct);

            return SyncResult.Nothing(connection.Id);
        }
    }

    private static async Task<SyncResult> ReconcileAsync(
        AsyncServiceScope scope,
        IntegrationsDbContext context,
        ExternalConnection connection,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        var credentials = scope.ServiceProvider.GetRequiredService<IIntegrationCredentials>();
        var directory = scope.ServiceProvider.GetRequiredService<IDirectoryReferenceReader>();

        var provider = scope.ServiceProvider
            .GetServices<IExternalWorkItemProvider>()
            .FirstOrDefault(candidate => candidate.Provider == connection.Provider)
            ?? throw new InvalidOperationException(
                $"No provider is registered for '{connection.Provider}'.");

        var credential = credentials.Resolve(connection.AuthRef);

        if (!credential.IsUsable)
        {
            // Thrown rather than skipped, so it lands on the connection as a failure an administrator can see.
            // A silently skipped pull is indistinguishable from an external system with nothing to report.
            throw new InvalidOperationException(
                $"No usable credential is configured for auth_ref '{connection.AuthRef}'.");
        }

        var snapshots = await provider.FetchAsync(
            new ProviderConnection(
                connection.Id,
                connection.BaseUrl,
                connection.ProjectOrQueue,
                connection.CurrentSprint,
                credential),
            ct);

        var mappings = connection.Mappings.ToList();

        // One resolution pass for the whole pull rather than a lookup per item: a sprint of two hundred tasks
        // held by a dozen people is twelve distinct uids, and asking the directory two hundred times would be
        // two hundred round trips to answer twelve questions.
        var uids = snapshots
            .Select(snapshot => snapshot.AssignedToLdapUid)
            .Where(uid => !string.IsNullOrEmpty(uid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        var people = await directory.ResolvePeopleByLdapUidAsync(uids!, ct);

        var now = DateTimeOffset.UtcNow;

        var existing = await context.WorkItems
            .Where(item => item.ConnectionId == connection.Id)
            .AsTracking()
            .ToDictionaryAsync(item => item.ExternalId, StringComparer.Ordinal, ct);

        var created = 0;
        var updated = 0;

        foreach (var snapshot in snapshots)
        {
            if (existing.TryGetValue(snapshot.ExternalId, out var item))
            {
                MirrorMapper.Apply(item, snapshot, connection, mappings, people, now);
                updated++;
            }
            else
            {
                var fresh = MirrorMapper.Create(snapshot, connection, mappings, people, now);

                context.WorkItems.Add(fresh);
                existing[fresh.ExternalId] = fresh;
                created++;
            }
        }

        // What the pull did not see. The provider returns the connection's whole current set precisely so this
        // subtraction is possible: an item that has left it has been closed, moved to another queue or deleted
        // outright, and from here all three mean the same thing — it is not live work any more.
        var seen = snapshots.Select(snapshot => snapshot.ExternalId).ToHashSet(StringComparer.Ordinal);

        var closed = 0;

        foreach (var item in existing.Values)
        {
            if (seen.Contains(item.ExternalId) || item.MirrorState == MirrorStates.Closed)
            {
                continue;
            }

            item.MirrorState = MirrorStates.Closed;
            item.ClosedAt = now;
            closed++;
        }

        // Tracked explicitly: the module's contexts read untracked by default, and a connection row updated
        // without tracking would save nothing while reporting success.
        var tracked = await context.Connections.AsTracking().FirstAsync(row => row.Id == connection.Id, ct);

        tracked.LastSyncedAt = now;
        tracked.LastSyncStatus = SyncStatuses.Ok;
        tracked.LastSyncError = null;
        tracked.LastSyncItemCount = snapshots.Count;
        tracked.ModifiedAt = now;

        var result = new SyncResult(connection.Id, created, updated, closed, Stopwatch.GetElapsedTime(started));

        if (result.TotalChanges > 0)
        {
            context.Enqueue(new ExternalWorkItemsSynced(
                connection.Id,
                connection.Provider,
                connection.DepartmentId,
                created,
                updated,
                closed));
        }

        // One SaveChanges: the mirror, the connection's sync state and the outbox row commit together or not at
        // all. A connection that claims a successful pull whose items never landed is the one inconsistency
        // nobody would think to look for.
        await context.SaveChangesAsync(ct);

        return result;
    }

    /// <summary>
    /// Writes the failure onto the connection.
    /// </summary>
    /// <remarks>
    /// In its own context, because the one that threw may hold half-applied changes and saving those is exactly
    /// what the failure path must not do. The message is truncated to what the column holds and to what is fair
    /// to show: a provider's exception text, not a stack trace.
    /// </remarks>
    private async Task RecordFailureAsync(
        IntegrationsDbContext context,
        ExternalConnection connection,
        Exception exception,
        CancellationToken ct)
    {
        try
        {
            context.ChangeTracker.Clear();

            var tracked = await context.Connections.AsTracking().FirstOrDefaultAsync(row => row.Id == connection.Id, ct);

            if (tracked is null)
            {
                return;
            }

            tracked.LastSyncStatus = SyncStatuses.Failed;
            tracked.LastSyncError = Truncate(exception.Message, 1024);
            tracked.ModifiedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);
        }
        catch (Exception nested) when (nested is not OperationCanceledException)
        {
            // The database is what just failed, in all likelihood. The pull is already logged and counted; there
            // is nothing further to gain by letting the bookkeeping of a failure fail the process.
            logger.LogWarning(nested, "Could not record the sync failure on connection {ConnectionId}", connection.Id);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static async Task<List<ExternalConnection>> LoadAsync(
        IntegrationsDbContext context,
        System.Linq.Expressions.Expression<Func<ExternalConnection, bool>> predicate,
        CancellationToken ct) =>
        await context.Connections
            .Where(predicate)
            .Include(connection => connection.Mappings)
            .ToListAsync(ct);

    private AsyncServiceScope SystemScope()
    {
        var scope = scopeFactory.CreateAsyncScope();

        // Set before anything opens a connection: the RLS interceptor stamps whatever the context holds at that
        // moment, and a scope stamped after the first query would run the rest of the pull as nobody.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        return scope;
    }
}
