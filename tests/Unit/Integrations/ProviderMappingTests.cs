using System.Net;
using System.Text;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cracra.Tests.Unit.Integrations;

/// <summary>
/// Each provider's DTO mapping, against recorded responses.
/// </summary>
/// <remarks>
/// The payloads below are the shapes Azure DevOps and ServiceNow actually return, trimmed to the fields the
/// mirror stores. Recorded rather than live, because a test that needs a DevOps collection is a test nobody runs
/// — and because the interesting cases here are the awkward ones: an identity object instead of a string, a
/// timestamp with no offset, a field that is simply absent.
/// </remarks>
public sealed class ProviderMappingTests
{
    private static readonly ProviderConnection DevOps = new(
        Guid.Parse("e0000000-0000-0000-0000-000000000001"),
        "https://devops.intranet",
        "CRACRA",
        "Sprint 42",
        new IntegrationCredential(IntegrationCredential.PersonalAccessToken, "token"));

    private static readonly ProviderConnection ServiceNow = new(
        Guid.Parse("e0000000-0000-0000-0000-000000000002"),
        "https://cracra.service-now.com",
        "Helpdesk N1",
        null,
        new IntegrationCredential(IntegrationCredential.Basic, "svc:password"));

    // --- Azure DevOps ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_devops_work_item_maps_onto_a_snapshot()
    {
        var items = await FetchDevOpsAsync();

        var item = items.ShouldHaveSingleItem();

        item.ExternalId.ShouldBe("4301");
        item.Reference.ShouldBe("AB#4301");
        item.Title.ShouldBe("Migrer le socle vers .NET 10");
        item.Type.ShouldBe("Task");
        item.State.ShouldBe("Active");
        item.EstimatedHours.ShouldBe(6m);
        item.UpdatedAtSource.ShouldBe(new DateTimeOffset(2026, 8, 19, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task The_assignee_is_reduced_to_the_uid_the_directory_keys_on()
    {
        var item = (await FetchDevOpsAsync()).ShouldHaveSingleItem();

        // DevOps reports a UPN inside an identity object. The directory keys on the LDAP uid, and the reduction
        // has to happen here or every pulled item arrives unassigned.
        item.AssignedToLdapUid.ShouldBe("camille.villeneuve");
    }

    [Fact]
    public async Task The_sprint_is_the_leaf_of_the_iteration_path()
    {
        var item = (await FetchDevOpsAsync()).ShouldHaveSingleItem();

        // "CRACRA\Release 3\Sprint 42" in a dropdown is mostly punctuation.
        item.SprintOrQueue.ShouldBe("Sprint 42");
    }

    [Fact]
    public async Task Both_the_area_and_the_iteration_are_offered_to_the_mapping_table_in_that_order()
    {
        var item = (await FetchDevOpsAsync()).ShouldHaveSingleItem();

        item.MappingHints.Count.ShouldBe(2);
        item.MappingHints[0].Kind.ShouldBe(MappingKinds.AreaPath);
        item.MappingHints[1].Kind.ShouldBe(MappingKinds.Iteration);
    }

    [Fact]
    public async Task An_unassigned_item_has_no_uid_rather_than_an_empty_one()
    {
        // System.AssignedTo is absent, not null, when nobody holds the item — a distinction that has caught out
        // every DevOps client ever written.
        var items = await FetchDevOpsAsync(fields: """
            "System.Id": 4304,
            "System.Title": "Documenter le pipeline",
            "System.WorkItemType": "Task",
            "System.State": "New",
            "System.AreaPath": "CRACRA\\Platform",
            "System.IterationPath": "CRACRA\\Sprint 42",
            "System.ChangedDate": "2026-08-19T14:30:00Z"
            """);

        var item = items.ShouldHaveSingleItem();

        item.AssignedToLdapUid.ShouldBeNull();
        item.EstimatedHours.ShouldBeNull();
    }

    [Fact]
    public async Task A_query_that_matches_nothing_costs_one_request_and_returns_nothing()
    {
        var recorder = new RecordingHandler(new Dictionary<string, string>
        {
            ["/_apis/wit/wiql"] = """{ "queryType": "flat", "workItems": [] }""",
        });

        var items = await new AzureDevOpsProvider(new HttpClient(recorder), NullLogger<AzureDevOpsProvider>.Instance)
            .FetchAsync(DevOps, TestContext.Current.CancellationToken);

        items.ShouldBeEmpty();

        // The batch call is skipped entirely. Asking DevOps for the fields of no work items answers 400, which
        // would have turned an empty sprint into a failed connection.
        recorder.Requests.Count.ShouldBe(1);
    }

    // --- ServiceNow ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_servicenow_task_maps_onto_a_snapshot()
    {
        var items = await FetchServiceNowAsync();

        var incident = items[0];

        incident.ExternalId.ShouldBe("a1b2c3");
        incident.Reference.ShouldBe("INC0010023");
        incident.Title.ShouldBe("Poste bloqué au démarrage");

        // sys_class_name is the table the record lives in, which is the closest thing ServiceNow has to a type
        // and the word an agent recognizes.
        incident.Type.ShouldBe("incident");
        incident.State.ShouldBe("2");
        incident.AssignedToLdapUid.ShouldBeNull();
    }

    [Fact]
    public async Task A_dot_walked_assignee_is_normalized_like_any_other()
    {
        var items = await FetchServiceNowAsync();

        // ServiceNow's user_name may carry a domain prefix on an instance federated with AD.
        items[1].AssignedToLdapUid.ShouldBe("thomas.berthier");
    }

    [Fact]
    public async Task A_timestamp_without_an_offset_is_read_as_utc()
    {
        var items = await FetchServiceNowAsync();

        // The instance's zone is not something this process knows, and assuming the host's would make the same
        // record mirror differently depending on which machine ran the sync.
        items[0].UpdatedAtSource.ShouldBe(new DateTimeOffset(2026, 8, 19, 6, 45, 12, TimeSpan.Zero));
    }

    [Fact]
    public async Task The_queue_is_the_items_own_group_and_it_is_offered_as_a_mapping_hint()
    {
        var items = await FetchServiceNowAsync();

        items[0].SprintOrQueue.ShouldBe("Helpdesk N1");
        items[0].MappingHints.ShouldHaveSingleItem().Kind.ShouldBe(MappingKinds.AssignmentGroup);
    }

    [Fact]
    public async Task Time_worked_never_becomes_an_estimate()
    {
        var items = await FetchServiceNowAsync();

        // Effort already spent is not effort remaining. A work order claiming two hours because somebody spent
        // two is a number that misleads every lead who plans against it.
        items.ShouldAllBe(item => item.EstimatedHours == null);
    }

    // --- Fixture ---------------------------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchDevOpsAsync(string? fields = null)
    {
        fields ??= """
            "System.Id": 4301,
            "System.Title": "Migrer le socle vers .NET 10",
            "System.WorkItemType": "Task",
            "System.State": "Active",
            "System.AssignedTo": {
              "displayName": "Camille Villeneuve",
              "uniqueName": "camille.villeneuve@cracra.example"
            },
            "System.AreaPath": "CRACRA\\Platform",
            "System.IterationPath": "CRACRA\\Release 3\\Sprint 42",
            "System.ChangedDate": "2026-08-19T14:30:00Z",
            "Microsoft.VSTS.Scheduling.RemainingWork": 6
            """;

        var handler = new RecordingHandler(new Dictionary<string, string>
        {
            ["/_apis/wit/wiql"] = """{ "queryType": "flat", "workItems": [{ "id": 4301 }] }""",
            ["/_apis/wit/workitems"] = $$"""{ "count": 1, "value": [{ "id": 4301, "fields": { {{fields}} } }] }""",
        });

        return await new AzureDevOpsProvider(new HttpClient(handler), NullLogger<AzureDevOpsProvider>.Instance)
            .FetchAsync(DevOps, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<ExternalWorkItemSnapshot>> FetchServiceNowAsync()
    {
        var handler = new RecordingHandler(new Dictionary<string, string>
        {
            ["/api/now/table/task"] = """
                {
                  "result": [
                    {
                      "sys_id": "a1b2c3",
                      "number": "INC0010023",
                      "short_description": "Poste bloqué au démarrage",
                      "sys_class_name": "incident",
                      "state": "2",
                      "assigned_to.user_name": "",
                      "assignment_group.name": "Helpdesk N1",
                      "sys_updated_on": "2026-08-19 06:45:12"
                    },
                    {
                      "sys_id": "d4e5f6",
                      "number": "REQ0004501",
                      "short_description": "Accès VPN pour un prestataire",
                      "sys_class_name": "sc_request",
                      "state": "1",
                      "assigned_to.user_name": "CRACRA\\Thomas.Berthier",
                      "assignment_group.name": "Helpdesk N1",
                      "sys_updated_on": "2026-08-19 05:10:00"
                    }
                  ]
                }
                """,
        });

        return await new ServiceNowProvider(new HttpClient(handler), NullLogger<ServiceNowProvider>.Instance)
            .FetchAsync(ServiceNow, TestContext.Current.CancellationToken);
    }

    /// <summary>Answers recorded payloads by path, and remembers what was asked.</summary>
    private sealed class RecordingHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);

            var path = request.RequestUri!.AbsolutePath;

            var body = responses.FirstOrDefault(entry =>
                path.EndsWith(entry.Key, StringComparison.OrdinalIgnoreCase)).Value;

            return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
