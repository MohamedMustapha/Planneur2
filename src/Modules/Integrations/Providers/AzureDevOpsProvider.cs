using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Integrations.Providers;

/// <summary>
/// BUILD work, pulled out of Azure DevOps.
/// </summary>
/// <remarks>
/// <para>
/// Two calls, because that is the shape of the API: WIQL answers with ids, and the fields come back from a batch
/// GET. Both are reads. The first is a POST only because a work-item query does not fit in a URL — see
/// <see cref="ReadOnlyHttpHandler"/>, which is what makes that exception a declared one rather than a hole.
/// </para>
/// <para>
/// The query asks for the team project's open items rather than only the caller's. "Assigned to me" is answered
/// from the mirror, against a resolved person id, not from the source: a query per user would be one round trip
/// per dropdown open, and would tie the feed's availability to DevOps being up at that moment.
/// </para>
/// </remarks>
internal sealed class AzureDevOpsProvider(HttpClient http, ILogger<AzureDevOpsProvider> logger)
    : IExternalWorkItemProvider
{
    /// <summary>DevOps refuses a batch larger than this, and answers 400 rather than truncating.</summary>
    private const int BatchSize = 200;

    private const string ApiVersion = "7.1";

    public string Provider => Contracts.ExternalProviders.AzureDevOps;

    public async Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchAsync(
        ProviderConnection connection,
        CancellationToken ct)
    {
        var ids = await QueryIdsAsync(connection, ct);

        if (ids.Count == 0)
        {
            return [];
        }

        var snapshots = new List<ExternalWorkItemSnapshot>(ids.Count);

        foreach (var batch in ids.Chunk(BatchSize))
        {
            snapshots.AddRange(await FetchBatchAsync(connection, batch, ct));
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<long>> QueryIdsAsync(ProviderConnection connection, CancellationToken ct)
    {
        // Closed and Removed are excluded at the source rather than mirrored and filtered here. The mirror is
        // meant to hold work somebody could still pick up; an organization's decade of closed tasks is not that,
        // and pulling it would make the first sync of a real project a very long afternoon.
        var wiql = new
        {
            query =
                "SELECT [System.Id] FROM WorkItems "
                + $"WHERE [System.TeamProject] = '{Escape(connection.ProjectOrQueue)}' "
                + "AND [System.State] NOT IN ('Closed', 'Removed', 'Done') "
                + "ORDER BY [System.ChangedDate] DESC",
        };

        var url = $"{Trim(connection.BaseUrl)}/{Uri.EscapeDataString(connection.ProjectOrQueue)}"
                  + $"/_apis/wit/wiql?api-version={ApiVersion}";

        using var request = Authorized(HttpMethod.Post, url, connection);

        request.Content = JsonContent.Create(wiql);

        using var response = await http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WiqlResult>(ct);

        return result?.WorkItems is null ? [] : [.. result.WorkItems.Select(reference => reference.Id)];
    }

    private async Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchBatchAsync(
        ProviderConnection connection,
        IReadOnlyList<long> ids,
        CancellationToken ct)
    {
        // Named fields rather than $expand=all: the mirror stores six of them, and asking for everything means
        // pulling descriptions and revision histories across the wire for a dropdown that shows a title.
        const string Fields =
            "System.Id,System.Title,System.WorkItemType,System.State,System.AssignedTo,"
            + "System.AreaPath,System.IterationPath,System.ChangedDate,Microsoft.VSTS.Scheduling.RemainingWork";

        var url = $"{Trim(connection.BaseUrl)}/_apis/wit/workitems"
                  + $"?ids={string.Join(',', ids)}&fields={Fields}&api-version={ApiVersion}";

        using var request = Authorized(HttpMethod.Get, url, connection);

        using var response = await http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var batch = await response.Content.ReadFromJsonAsync<WorkItemBatch>(ct);

        if (batch?.Value is null)
        {
            logger.LogWarning(
                "Azure DevOps returned no body for a batch of {Count} work items on connection {ConnectionId}",
                ids.Count,
                connection.ConnectionId);

            return [];
        }

        return [.. batch.Value.Select(item => Map(connection, item))];
    }

    private static ExternalWorkItemSnapshot Map(ProviderConnection connection, WorkItem item)
    {
        var fields = item.Fields;

        var areaPath = Text(fields, "System.AreaPath");
        var iterationPath = Text(fields, "System.IterationPath");

        // Both offered, in that order. A team that organizes by area gets its project from the area; one that
        // organizes by iteration gets it from the iteration; a team that maps both gets the area, because it is
        // the more stable of the two — an iteration is renamed every fortnight.
        var hints = new List<MappingHint>(2);

        if (!string.IsNullOrWhiteSpace(areaPath))
        {
            hints.Add(new MappingHint(Domain.MappingKinds.AreaPath, areaPath));
        }

        if (!string.IsNullOrWhiteSpace(iterationPath))
        {
            hints.Add(new MappingHint(Domain.MappingKinds.Iteration, iterationPath));
        }

        return new ExternalWorkItemSnapshot(
            item.Id.ToString(),
            $"AB#{item.Id}",
            Text(fields, "System.Title") ?? $"Work item {item.Id}",
            Text(fields, "System.WorkItemType") ?? "Task",
            Text(fields, "System.State") ?? "New",
            LdapUid.FromDevOpsIdentity(Identity(fields, "System.AssignedTo")),

            // The sprint the item is in, which is the leaf of the iteration path. DevOps reports the whole path,
            // and "CRACRA\Release 3\Sprint 42" in a dropdown is mostly punctuation.
            Leaf(iterationPath),
            hints,

            // The API's own url is the REST resource, which is not something to put in front of a person. The
            // web link is derivable and stable, so it is derived.
            WebUrl(connection, item.Id),
            Decimal(fields, "Microsoft.VSTS.Scheduling.RemainingWork"),
            Timestamp(fields, "System.ChangedDate"));
    }

    /// <summary>
    /// A request carrying this connection's credential and nothing of anyone else's.
    /// </summary>
    /// <remarks>
    /// Per request rather than on the client's default headers. The typed client is shared across every DevOps
    /// connection in the process, and a token left on the defaults would be one department's PAT used for
    /// another department's pull — sometimes successfully, which is the worst version of that bug.
    /// </remarks>
    private static HttpRequestMessage Authorized(HttpMethod method, string url, ProviderConnection connection)
    {
        var request = new HttpRequestMessage(method, url);

        request.Headers.Authorization = ReadOnlyHttpHandler.Authorization(connection.Credential);

        return request;
    }

    private static string Trim(string baseUrl) => baseUrl.TrimEnd('/');

    /// <summary>WIQL is single-quoted; a project called <c>L'Atelier</c> would otherwise end the string early.</summary>
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string? Leaf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Split('\\')[^1];

    private static string WebUrl(ProviderConnection connection, long id) =>
        $"{Trim(connection.BaseUrl)}/{Uri.EscapeDataString(connection.ProjectOrQueue)}/_workitems/edit/{id}";

    private static string? Text(IReadOnlyDictionary<string, JsonElement>? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? Decimal(IReadOnlyDictionary<string, JsonElement>? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    private static DateTimeOffset? Timestamp(IReadOnlyDictionary<string, JsonElement>? fields, string name)
    {
        var text = Text(fields, name);

        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    /// <summary>System.AssignedTo is an identity object, not a string, and is absent when nobody is assigned.</summary>
    private static string? Identity(IReadOnlyDictionary<string, JsonElement>? fields, string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value.TryGetProperty("uniqueName", out var uniqueName) && uniqueName.ValueKind == JsonValueKind.String
            ? uniqueName.GetString()
            : null;
    }

    private sealed record WiqlResult(
        [property: JsonPropertyName("workItems")] List<WorkItemReference>? WorkItems);

    private sealed record WorkItemReference([property: JsonPropertyName("id")] long Id);

    private sealed record WorkItemBatch([property: JsonPropertyName("value")] List<WorkItem>? Value);

    private sealed record WorkItem(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("fields")] Dictionary<string, JsonElement>? Fields);
}

/// <summary>
/// Turning what a source calls a person into the uid the directory keys on.
/// </summary>
/// <remarks>
/// Both systems report identity in a shape of their own — DevOps a UPN, ServiceNow a user_name that may carry a
/// domain prefix — and both reduce to the LDAP <c>uid</c> Keycloak surfaces as the username. Normalizing here
/// rather than at resolution time means the stored <c>assigned_to_ldap_uid</c> is comparable across providers,
/// which is what lets one resolution pass serve both.
/// </remarks>
internal static class LdapUid
{
    public static string? FromDevOpsIdentity(string? uniqueName) => Normalize(uniqueName);

    public static string? Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var value = candidate.Trim();

        // DOMAIN\uid — the on-prem shape, still common in a TFS-era collection.
        var slash = value.LastIndexOf('\\');

        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        // uid@corp.example — the cloud shape.
        var at = value.IndexOf('@', StringComparison.Ordinal);

        if (at > 0)
        {
            value = value[..at];
        }

        return value.Length == 0 ? null : value.ToLowerInvariant();
    }
}
