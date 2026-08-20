namespace Cracra.Modules.Integrations.Providers;

// =================================================================================================================
// The provider seam.
//
// One interface, one method, no verb but "fetch". S10's single rule — the platform never writes back — is
// enforced in three independent places, and this is the first of them: there is no method here that could push
// anything anywhere. The second is ReadOnlyHttpHandler, which refuses a mutating request at the socket. The third
// is the architecture test, which reads this assembly's metadata and fails the build if anything so much as
// references HttpMethod.Put.
//
// Three, because each catches a different mistake. An absent method stops a design drifting; the handler stops a
// hand-rolled HttpRequestMessage; the metadata test stops both being quietly undone by somebody who had a good
// afternoon reason.
// =================================================================================================================

/// <summary>What a provider needs to know to pull, with nothing of our persistence in it.</summary>
/// <remarks>
/// A neutral record rather than the <c>ExternalConnection</c> entity so a provider can be exercised — and is, in
/// the unit tests — without a database, and so no adapter is ever one field away from writing to a tracked row.
/// </remarks>
public sealed record ProviderConnection(
    Guid ConnectionId,
    string BaseUrl,
    string ProjectOrQueue,
    string? CurrentSprint,
    IntegrationCredential Credential);

/// <summary>
/// A vocabulary value the source used, offered to the mapping table.
/// </summary>
/// <remarks>
/// The provider reports what it has — an area path, an iteration, an assignment group — and says nothing about
/// which local project or unit that means. Resolution belongs to the synchronizer, which is the only place that
/// can see every department's mappings; a provider that resolved would need to, and would then be one bug away
/// from mirroring one department's tickets into another's queue.
/// </remarks>
public sealed record MappingHint(string Kind, string Value);

/// <summary>One work item as a provider found it. Everything here is the source's own vocabulary.</summary>
public sealed record ExternalWorkItemSnapshot(
    string ExternalId,
    string Reference,
    string Title,
    string Type,
    string State,
    string? AssignedToLdapUid,
    string? SprintOrQueue,
    IReadOnlyList<MappingHint> MappingHints,
    string? Url,
    decimal? EstimatedHours,
    DateTimeOffset? UpdatedAtSource);

/// <summary>
/// Reads work items out of one external system.
/// </summary>
/// <remarks>
/// Fetch is the whole interface, and deliberately returns the connection's <em>current</em> set rather than a
/// delta. The synchronizer needs the full set to know what has left it — a delta API tells you what changed, not
/// what is gone, and "gone" is exactly the fact the stale-close pass exists to record.
/// </remarks>
public interface IExternalWorkItemProvider
{
    /// <summary>azure-devops | servicenow.</summary>
    string Provider { get; }

    Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchAsync(ProviderConnection connection, CancellationToken ct);
}

/// <summary>
/// A credential, resolved from the deployment's secret store by the connection's <c>auth_ref</c>.
/// </summary>
/// <remarks>
/// Kind rather than a ready-made header, because the two providers authenticate differently and the difference
/// belongs with the provider that knows why: a DevOps PAT goes in a Basic header with an empty username, which
/// looks like a bug anywhere except next to the DevOps client.
/// </remarks>
public sealed record IntegrationCredential(string Kind, string Value)
{
    /// <summary>Azure DevOps personal access token.</summary>
    public const string PersonalAccessToken = "pat";

    /// <summary>user:password, as ServiceNow's Table API expects.</summary>
    public const string Basic = "basic";

    /// <summary>An OAuth access token the deployment refreshes out of band.</summary>
    public const string Bearer = "bearer";

    /// <summary>Nothing configured. Kept as a value rather than a null so a caller must decide what to do.</summary>
    public static readonly IntegrationCredential None = new("none", string.Empty);

    public bool IsUsable => Kind != "none" && !string.IsNullOrWhiteSpace(Value);
}
