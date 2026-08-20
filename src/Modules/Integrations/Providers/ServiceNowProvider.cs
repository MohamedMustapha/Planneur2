using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Integrations.Providers;

/// <summary>
/// RUN work, pulled out of ServiceNow.
/// </summary>
/// <remarks>
/// <para>
/// One call to the Table API against <c>task</c>, which is the parent of both incidents and requests — the two
/// things S10 names. Querying the parent means a deployment that also raises change tasks gets them without a
/// code change, and the item's own <c>sys_class_name</c> is what the mirror stores as its type.
/// </para>
/// <para>
/// Both the assigned and the unassigned come back in one pull. S6a's pool wants what nobody has picked up and
/// S5's dropdown wants an agent's own, and they are the same query with a different predicate applied to the
/// mirror afterwards — which is where every predicate in this platform is applied.
/// </para>
/// </remarks>
internal sealed class ServiceNowProvider(HttpClient http, ILogger<ServiceNowProvider> logger)
    : IExternalWorkItemProvider
{
    /// <summary>A page the instance will actually return. Beyond this ServiceNow starts refusing outright.</summary>
    private const int PageSize = 500;

    public string Provider => Contracts.ExternalProviders.ServiceNow;

    public async Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchAsync(
        ProviderConnection connection,
        CancellationToken ct)
    {
        // Dot-walked fields, so one request carries the assignee's user_name and the group's name rather than
        // sys_id references we would then have to resolve one lookup at a time.
        const string Fields =
            "sys_id,number,short_description,sys_class_name,state,assigned_to.user_name,"
            + "assignment_group.name,sys_updated_on,time_worked";

        var query = $"assignment_group.name={connection.ProjectOrQueue}^active=true^ORDERBYDESCsys_updated_on";

        var url = $"{connection.BaseUrl.TrimEnd('/')}/api/now/table/task"
                  + $"?sysparm_query={Uri.EscapeDataString(query)}"
                  + $"&sysparm_fields={Fields}"
                  // display_value=false keeps state and class as the codes the instance stores. The display
                  // values are localized per user, and a mirror whose contents depend on the service account's
                  // language preference is a mirror nobody can query.
                  + "&sysparm_display_value=false"
                  + "&sysparm_exclude_reference_link=true"
                  + $"&sysparm_limit={PageSize}";

        // The credential goes on the request, not on the client's defaults: one typed client serves every
        // ServiceNow connection in the process, and a shared default header would be one department's service
        // account pulling another department's queue.
        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Headers = { Authorization = ReadOnlyHttpHandler.Authorization(connection.Credential) },
        };

        using var response = await http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<TableResponse>(ct);

        if (page?.Result is null)
        {
            logger.LogWarning(
                "ServiceNow returned no result array for connection {ConnectionId}; treating the pull as empty",
                connection.ConnectionId);

            return [];
        }

        return [.. page.Result.Select(record => Map(connection, record))];
    }

    private static ExternalWorkItemSnapshot Map(ProviderConnection connection, TaskRecord record)
    {
        var group = record.AssignmentGroup;

        return new ExternalWorkItemSnapshot(
            record.SysId,
            record.Number ?? record.SysId,
            record.ShortDescription ?? record.Number ?? record.SysId,

            // sys_class_name is the table the record actually lives in: incident, sc_request, change_task. It is
            // the closest thing ServiceNow has to a type, and it is what an agent recognizes.
            record.Class ?? "task",
            record.State ?? "1",
            LdapUid.Normalize(record.AssignedTo),

            // The queue is the item's own group rather than the connection's, because a task can be reassigned
            // between groups and the mirror should say where it is now.
            group ?? connection.ProjectOrQueue,
            group is null ? [] : [new MappingHint(Domain.MappingKinds.AssignmentGroup, group)],
            $"{connection.BaseUrl.TrimEnd('/')}/nav_to.do?uri=task.do?sys_id={record.SysId}",

            // time_worked is seconds of effort already spent, not an estimate. Deliberately not mapped to the
            // pool's estimated hours: a work order that claims two hours because somebody spent two is a number
            // that will mislead every lead who plans against it.
            EstimatedHours: null,
            Timestamp(record.UpdatedOn));
    }

    /// <summary>
    /// ServiceNow stamps <c>sys_updated_on</c> in the instance's own timezone, without an offset.
    /// </summary>
    /// <remarks>
    /// Parsed as UTC rather than as local. The instance's zone is not something this process knows, and assuming
    /// the server's would make the same record mirror differently depending on which host ran the sync. UTC is
    /// wrong by a fixed, visible amount; local is wrong by an amount that changes with the deployment.
    /// </remarks>
    private static DateTimeOffset? Timestamp(string? value) =>
        DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;

    private sealed record TableResponse([property: JsonPropertyName("result")] List<TaskRecord>? Result);

    private sealed record TaskRecord(
        [property: JsonPropertyName("sys_id")] string SysId,
        [property: JsonPropertyName("number")] string? Number,
        [property: JsonPropertyName("short_description")] string? ShortDescription,
        [property: JsonPropertyName("sys_class_name")] string? Class,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("assigned_to.user_name")] string? AssignedTo,
        [property: JsonPropertyName("assignment_group.name")] string? AssignmentGroup,
        [property: JsonPropertyName("sys_updated_on")] string? UpdatedOn);
}
