using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// Configures a connection against the dev box's provider stub, the way an administrator would.
/// </summary>
/// <remarks>
/// <para>
/// Shared because two journeys need it: S10's own, which is about configuring and pulling, and S5's, whose
/// dropdown has nothing in it until somebody has. Before S10 that dropdown was fed by a sample source the dev box
/// turned on; now it is fed by a mirror, and a mirror is empty until a connection exists — which is the correct
/// behaviour and does mean the arrangement is real work.
/// </para>
/// <para>
/// Everything goes through the API on a head's own signed-in session: the same BFF, the same policies, the same
/// RLS. A precondition set up behind the application's back can arrange states the application would never allow,
/// and this one — a department head configuring their own department's integration — is precisely the state the
/// journey is about.
/// </para>
/// </remarks>
internal static class ExternalConnections
{
    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";
    private const string Infrastructure = "aaaaaaaa-0000-0000-0000-000000000001";

    /// <summary>The area path the stub reports on every work item, and the sprint it calls current.</summary>
    public const string AreaPath = @"CRACRA\Platform";

    public const string CurrentSprint = "Sprint 42";

    /// <summary>The stub's assignment group, matching what the ServiceNow adapter queries for.</summary>
    public const string Queue = "Helpdesk N1";

    /// <summary>A connection this journey configured. The name is how a test finds its card on the screen.</summary>
    internal sealed record Configured(string Id, string Name);

    /// <summary>A DevOps connection whose area path maps to <paramref name="projectId"/>, pulled once.</summary>
    public static async Task<Configured> DevOpsAsync(AspireStackFixture stack, IPage head, string projectId)
    {
        var connection = await CreateAsync(stack, head, "azure-devops", "CRACRA", "dev-devops", CurrentSprint);

        await MapAsync(head, connection.Id, "area-path", AreaPath, projectId: projectId);
        await SyncAsync(head, connection.Id);

        return connection;
    }

    /// <summary>A ServiceNow connection whose assignment group maps to the Infrastructure unit, pulled once.</summary>
    public static async Task<Configured> ServiceNowAsync(AspireStackFixture stack, IPage head)
    {
        var connection = await CreateAsync(stack, head, "servicenow", Queue, "dev-servicenow", currentSprint: null);

        await MapAsync(head, connection.Id, "assignment-group", Queue, unitId: Infrastructure);
        await SyncAsync(head, connection.Id);

        return connection;
    }

    private static async Task<Configured> CreateAsync(
        AspireStackFixture stack,
        IPage head,
        string provider,
        string projectOrQueue,
        string authRef,
        string? currentSprint)
    {
        // Unique, because the Aspire stack persists between runs and two journeys configuring "the" DevOps
        // connection would otherwise be talking about different rows in an order nobody controls.
        var name = $"{provider} {Guid.CreateVersion7().ToString("N")[^8..]}";

        var response = await head.APIRequest.PostAsync("/api/integrations/connections", new APIRequestContextOptions
        {
            // The BFF refuses a proxied /api call without this. The session is a cookie, which a cross-site
            // request would send automatically but could not add a header to — so arranging state plays by the
            // same rule everything else does.
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["nodeId"] = InformationSystems,
                ["provider"] = provider,
                ["name"] = name,

                // The stub's own address, exactly as a real collection URL would be given. Nothing in the
                // platform's code path knows the difference.
                ["baseUrl"] = stack.ProvidersBaseUrl,
                ["authRef"] = authRef,
                ["projectOrQueue"] = projectOrQueue,
                ["currentSprint"] = currentSprint,

                // On demand: the journey pulls when it is ready to, and a schedule racing the assertions would
                // make the failures depend on how long Keycloak took to answer.
                ["pollInterval"] = "00:00:00",
                ["active"] = true,
            },
        });

        response.Status.ShouldBe(201);

        return new Configured((await response.JsonAsync())!.Value.GetProperty("id").GetString()!, name);
    }

    private static async Task MapAsync(
        IPage head,
        string connectionId,
        string kind,
        string externalValue,
        string? projectId = null,
        string? unitId = null)
    {
        var response = await head.APIRequest.PostAsync(
            $"/api/integrations/connections/{connectionId}/mappings",
            new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["kind"] = kind,
                    ["externalValue"] = externalValue,
                    ["projectId"] = projectId,
                    ["unitId"] = unitId,
                },
            });

        response.Status.ShouldBe(201);
    }

    /// <summary>
    /// Asks for a pull and waits for it to have happened.
    /// </summary>
    /// <remarks>
    /// The endpoint answers 202 and the work runs behind it, which is right for the product and inconvenient for
    /// a test: asserting immediately would assert against a mirror that is still empty. Polling the connection's
    /// own last-sync status is how a person would find out too, so the wait is on the same fact the screen shows
    /// rather than on a sleep long enough to usually work.
    /// </remarks>
    private static async Task SyncAsync(IPage head, string connectionId)
    {
        var accepted = await head.APIRequest.PostAsync(
            $"/api/integrations/{connectionId}/sync",
            new APIRequestContextOptions { Headers = AntiForgery, DataObject = new Dictionary<string, object?>() });

        accepted.Status.ShouldBe(202);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            // The header on a GET too: the BFF demands it on every proxied /api call, not only on writes, which
            // is what makes the cookie session safe to use at all.
            var response = await head.APIRequest.GetAsync(
                $"/api/integrations/connections/{connectionId}",
                new APIRequestContextOptions { Headers = AntiForgery });

            response.Status.ShouldBe(200);

            // Read once. Playwright consumes the body, and a second JsonAsync() on the same response answers with
            // an empty element — which surfaces as a missing property and hides whatever actually went wrong.
            var connection = (await response.JsonAsync())!.Value;

            var status = connection.GetProperty("lastSyncStatus").GetString();

            if (status == "ok")
            {
                return;
            }

            if (status == "failed")
            {
                throw new InvalidOperationException(
                    $"The pull failed: {connection.GetProperty("lastSyncError").GetString()}");
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Connection {connectionId} never reported a completed pull.");
    }
}
