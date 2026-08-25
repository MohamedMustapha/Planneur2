using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Integrations.Services;

/// <summary>What an administrator supplies to create or update a connection.</summary>
public sealed record ConnectionRequest(
    Guid NodeId,
    string Provider,
    string Name,
    string BaseUrl,
    string AuthRef,
    string ProjectOrQueue,
    string? CurrentSprint,
    TimeSpan PollInterval,
    bool Active);

/// <summary>What an administrator supplies to declare a mapping.</summary>
public sealed record MappingRequest(string Kind, string ExternalValue, Guid? ProjectId, Guid? UnitId);

/// <summary>
/// A connection as the administration screen shows it.
/// </summary>
/// <remarks>
/// Carries <see cref="AuthRef"/> — the name of the secret — and never the secret. There is no shape of this
/// response that could leak a token, because the token is not in the module's database to begin with.
/// </remarks>
public sealed record ConnectionView(
    Guid Id,
    Guid NodeId,
    string Provider,
    string Name,
    string BaseUrl,
    string AuthRef,
    string ProjectOrQueue,
    string? CurrentSprint,
    TimeSpan PollInterval,
    bool Active,
    DateTimeOffset? LastSyncedAt,
    string LastSyncStatus,
    string? LastSyncError,
    int LastSyncItemCount,

    /// <summary>Open mirror rows this connection currently accounts for. What "is it working" looks like.</summary>
    int MirroredItemCount,
    IReadOnlyList<MappingView> Mappings);

public sealed record MappingView(Guid Id, string Kind, string ExternalValue, Guid? ProjectId, Guid? UnitId);

public interface IConnectionService
{
    /// <param name="nodeId">
    /// A branch, whose <em>effective</em> connections come back: the ones wired at it and the ones it inherits
    /// from above. Null lists everything the caller may read, which is the administrator's own view.
    /// </param>
    Task<IReadOnlyList<ConnectionView>> ListAsync(Guid? nodeId, CancellationToken ct);

    Task<ConnectionView> GetAsync(Guid id, CancellationToken ct);

    Task<ConnectionView> CreateAsync(ConnectionRequest request, CancellationToken ct);

    Task<ConnectionView> UpdateAsync(Guid id, ConnectionRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<MappingView> AddMappingAsync(Guid connectionId, MappingRequest request, CancellationToken ct);

    Task RemoveMappingAsync(Guid connectionId, Guid mappingId, CancellationToken ct);
}

/// <summary>
/// The configuration half of the module.
/// </summary>
/// <remarks>
/// <para>
/// Every read and write here runs on the caller's own connection, so the <c>can_write_connection</c> policy — not
/// this class — decides whose connections come back and whose may be edited. There is no department filter in
/// this code and there must not be one: the endpoint's policy says "a head may call me", and RLS says "of this
/// department".
/// </para>
/// <para>
/// What this class does check is what RLS cannot: that the provider is one we have an adapter for, that the poll
/// interval is sane, and that the base URL points somewhere the deployment permits. Those are facts about the
/// process, not about the caller.
/// </para>
/// </remarks>
internal sealed class ConnectionService(
    IntegrationsDbContext context,
    IOptions<IntegrationsOptions> options,
    IOrgNodeReader nodes,
    IIntegrationCapability capability,
    IUserContext user) : IConnectionService
{
    /// <summary>Below this a poll is a denial of service against somebody else's ticketing system.</summary>
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaximumPollInterval = TimeSpan.FromDays(1);

    public async Task<IReadOnlyList<ConnectionView>> ListAsync(Guid? nodeId, CancellationToken ct)
    {
        await capability.EnsureAvailableAsync(ct);

        // Inherited down, like a profile: a branch's effective connections are the ones wired at it plus the ones
        // wired anywhere above it. Asking for exact matches instead would make a service wired at the top
        // invisible to every branch that actually uses it.
        var path = nodeId is { } node ? await nodes.GetPathAsync(node, ct) : [];

        var connections = await context.Connections
            .Where(connection => nodeId == null || path.Contains(connection.NodeId))
            .Include(connection => connection.Mappings)
            .OrderBy(connection => connection.Provider)
            .ThenBy(connection => connection.Name)
            .ToListAsync(ct);

        return [.. await ProjectAsync(connections, ct)];
    }

    public async Task<ConnectionView> GetAsync(Guid id, CancellationToken ct)
    {
        await capability.EnsureAvailableAsync(ct);

        var connection = await FindAsync(id, ct);

        return (await ProjectAsync([connection], ct))[0];
    }

    public async Task<ConnectionView> CreateAsync(ConnectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await capability.EnsureAvailableAsync(ct);

        var now = DateTimeOffset.UtcNow;

        var connection = new ExternalConnection
        {
            Id = Guid.CreateVersion7(),
            NodeId = request.NodeId,
            Provider = Provider(request.Provider),
            Name = Required(request.Name, nameof(request.Name), 256),
            BaseUrl = BaseUrl(request.BaseUrl),
            AuthRef = Required(request.AuthRef, nameof(request.AuthRef), 128),
            ProjectOrQueue = Required(request.ProjectOrQueue, nameof(request.ProjectOrQueue), 256),
            CurrentSprint = Optional(request.CurrentSprint, 256),
            PollInterval = PollInterval(request.PollInterval),
            Active = request.Active,
            CreatedBy = user.UserId,
            CreatedAt = now,
            ModifiedAt = now,
        };

        context.Connections.Add(connection);

        await context.SaveChangesAsync(ct);

        return (await ProjectAsync([connection], ct))[0];
    }

    public async Task<ConnectionView> UpdateAsync(Guid id, ConnectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await capability.EnsureAvailableAsync(ct);

        var connection = await FindAsync(id, tracking: true, ct);

        // The branch is not editable. Moving a connection would move every mirror row it owns into another
        // branch's scope — silently, and for rows an activity somewhere already references. Deleting it and
        // configuring a new one makes that consequence visible, which is the point.
        if (request.NodeId != connection.NodeId)
        {
            throw new DomainRuleViolationException(
                "A connection cannot change branch. Delete it and configure one in the other branch.");
        }

        connection.Provider = Provider(request.Provider);
        connection.Name = Required(request.Name, nameof(request.Name), 256);
        connection.BaseUrl = BaseUrl(request.BaseUrl);
        connection.AuthRef = Required(request.AuthRef, nameof(request.AuthRef), 128);
        connection.ProjectOrQueue = Required(request.ProjectOrQueue, nameof(request.ProjectOrQueue), 256);
        connection.CurrentSprint = Optional(request.CurrentSprint, 256);
        connection.PollInterval = PollInterval(request.PollInterval);
        connection.Active = request.Active;
        connection.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        return (await ProjectAsync([connection], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var connection = await FindAsync(id, tracking: true, ct);

        // The mirror rows go with it, by cascade. They are a cache of somebody else's data and mean nothing
        // without the connection that explains where they came from; an activity that referenced one keeps its
        // external_ref, which is the source's own identifier and still resolves in the source.
        context.Connections.Remove(connection);

        await context.SaveChangesAsync(ct);

        IntegrationTelemetry.Forget(id);
    }

    public async Task<MappingView> AddMappingAsync(Guid connectionId, MappingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await FindAsync(connectionId, ct);

        var kind = Kind(request.Kind);

        // The same rule the check constraint enforces, stated here so the caller gets a 422 naming the problem
        // rather than a 500 carrying a constraint name. Both exist on purpose: this one explains, that one holds.
        if (MappingKinds.TargetsProject(kind))
        {
            if (request.ProjectId is null || request.UnitId is not null)
            {
                throw new DomainRuleViolationException($"A '{kind}' mapping targets a project, and only a project.");
            }
        }
        else if (request.UnitId is null || request.ProjectId is not null)
        {
            throw new DomainRuleViolationException($"A '{kind}' mapping targets a unit, and only a unit.");
        }

        var mapping = new ExternalMapping
        {
            Id = Guid.CreateVersion7(),
            ConnectionId = connection.Id,
            NodeId = connection.NodeId,
            Kind = kind,
            ExternalValue = Required(request.ExternalValue, nameof(request.ExternalValue), 512),
            ProjectId = request.ProjectId,
            UnitId = request.UnitId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.Mappings.Add(mapping);

        await context.SaveChangesAsync(ct);

        return Project(mapping);
    }

    public async Task RemoveMappingAsync(Guid connectionId, Guid mappingId, CancellationToken ct)
    {
        var mapping = await context.Mappings
            .AsTracking()
            .FirstOrDefaultAsync(row => row.Id == mappingId && row.ConnectionId == connectionId, ct)
            ?? throw new ResourceNotFoundException($"Mapping {mappingId} was not found.");

        context.Mappings.Remove(mapping);

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Counts the open mirror rows per connection in one query.
    /// </summary>
    /// <remarks>
    /// The number an administrator actually looks at: "last sync: ok, 0 items" and "last sync: ok, 214 items" are
    /// the same status and very different situations. Grouped rather than counted per row, because the list shows
    /// every connection in a department and N+1 on a settings screen is still N+1.
    /// </remarks>
    private async Task<IReadOnlyList<ConnectionView>> ProjectAsync(
        IReadOnlyList<ExternalConnection> connections,
        CancellationToken ct)
    {
        if (connections.Count == 0)
        {
            return [];
        }

        var ids = connections.Select(connection => connection.Id).ToList();

        var counts = await context.WorkItems
            .Where(item => ids.Contains(item.ConnectionId) && item.MirrorState == MirrorStates.Open)
            .GroupBy(item => item.ConnectionId)
            .Select(group => new { ConnectionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ConnectionId, entry => entry.Count, ct);

        return
        [
            .. connections.Select(connection => new ConnectionView(
                connection.Id,
                connection.NodeId,
                connection.Provider,
                connection.Name,
                connection.BaseUrl,
                connection.AuthRef,
                connection.ProjectOrQueue,
                connection.CurrentSprint,
                connection.PollInterval,
                connection.Active,
                connection.LastSyncedAt,
                connection.LastSyncStatus,
                connection.LastSyncError,
                connection.LastSyncItemCount,
                counts.GetValueOrDefault(connection.Id),
                [.. connection.Mappings.OrderBy(mapping => mapping.Kind).ThenBy(mapping => mapping.ExternalValue)
                    .Select(Project)])),
        ];
    }

    private static MappingView Project(ExternalMapping mapping) =>
        new(mapping.Id, mapping.Kind, mapping.ExternalValue, mapping.ProjectId, mapping.UnitId);

    private Task<ExternalConnection> FindAsync(Guid id, CancellationToken ct) => FindAsync(id, false, ct);

    private async Task<ExternalConnection> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = context.Connections.Include(connection => connection.Mappings).AsQueryable();

        if (tracking)
        {
            query = query.AsTracking();
        }

        // Not found and filtered-by-RLS are the same answer, deliberately: telling a Finance head that a
        // connection id exists but is not theirs is the disclosure the policy exists to prevent.
        return await query.FirstOrDefaultAsync(connection => connection.Id == id, ct)
               ?? throw new ResourceNotFoundException($"Connection {id} was not found.");
    }

    private static string Provider(string provider)
    {
        var normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();

        // An unknown provider would configure a connection nothing can pull, and the symptom would be a sync
        // that fails forever with "no provider registered" — true, and unhelpful at the point it is discovered.
        return ExternalProviders.IsKnown(normalized)
            ? normalized
            : throw new DomainRuleViolationException(
                $"'{provider}' is not a provider this platform integrates with.");
    }

    private static string Kind(string kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();

        return MappingKinds.All.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : throw new DomainRuleViolationException($"'{kind}' is not a mapping kind.");
    }

    private TimeSpan PollInterval(TimeSpan interval)
    {
        if (interval == TimeSpan.Zero)
        {
            // Explicitly allowed: a connection somebody wants to pull by hand only. Distinct from a tiny
            // interval, which is somebody misunderstanding the units.
            return TimeSpan.Zero;
        }

        return interval >= MinimumPollInterval && interval <= MaximumPollInterval
            ? interval
            : throw new DomainRuleViolationException(
                $"A poll interval must be zero, or between {MinimumPollInterval} and {MaximumPollInterval}.");
    }

    private string BaseUrl(string baseUrl)
    {
        var value = Required(baseUrl, nameof(baseUrl), 1024);

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainRuleViolationException("A connection's base URL must be an absolute http(s) URL.");
        }

        var allowed = options.Value.AllowedHosts;

        // Checked here as well as in the handler, because the two answer different questions. The handler stops
        // a request; this stops a configuration, at the moment somebody is in a position to correct it.
        if (allowed.Count > 0 && !allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                $"'{uri.Host}' is not an allow-listed integration endpoint for this deployment.");
        }

        return value;
    }

    private static string Required(string? value, string name, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleViolationException($"{name} is required.");
        }

        return trimmed.Length <= max
            ? trimmed
            : throw new DomainRuleViolationException($"{name} must be {max} characters or fewer.");
    }

    private static string? Optional(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= max
            ? trimmed
            : throw new DomainRuleViolationException($"The value must be {max} characters or fewer.");
    }
}
