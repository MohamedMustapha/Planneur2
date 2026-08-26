using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;

namespace Cracra.Tests.Integration;

/// <summary>
/// The S0 acceptance test: an authenticated request reaches the API and its RLS context is provably set.
/// </summary>
/// <remarks>
/// "Provably" is the operative word. Asserting that the endpoint returned 200 would only show that authentication
/// works; what the platform depends on is that the caller's identity made it all the way into the Postgres session,
/// which is why /api/ping reports what the database sees and these tests compare the two ends.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PingChainTests(PostgresFixture postgres)
{
    private sealed record PingResponse(
        string Service,
        DateTimeOffset UtcNow,
        PingIdentity Identity,
        PingDatabaseSession DatabaseSession);

    private sealed record PingIdentity(
        Guid UserId,
        string UserName,
        Guid? UnitId,
        IReadOnlyList<Guid> DepartmentIds,
        IReadOnlyList<string> Roles,
        string Language);

    private sealed record PingDatabaseSession(
        Guid? UserId,
        Guid? UnitId,
        IReadOnlyList<Guid> DepartmentIds,
        IReadOnlyList<string> Roles,
        bool IsScoped);

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsAnonymous();

        var response = await factory.CreateClient().GetAsync("/api/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_authenticated_caller_reaches_the_endpoint()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync("/api/ping", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_database_session_matches_the_caller_identity()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsUser(SeedOrganisation.Thomas);

        var ping = await factory.CreateClient()
            .GetFromJsonAsync<PingResponse>("/api/ping", TestContext.Current.CancellationToken);

        ping.ShouldNotBeNull();

        // Application side.
        ping.Identity.UserId.ShouldBe(SeedOrganisation.Thomas.UserId);
        ping.Identity.UnitId.ShouldBe(SeedOrganisation.Units.Infrastructure);
        ping.Identity.Roles.ShouldContain(ContextualRole.NodeHead);

        // Database side, read back through the same access.* helpers the RLS policies call.
        ping.DatabaseSession.IsScoped.ShouldBeTrue();
        ping.DatabaseSession.UserId.ShouldBe(SeedOrganisation.Thomas.UserId);
        ping.DatabaseSession.UnitId.ShouldBe(SeedOrganisation.Units.Infrastructure);
        ping.DatabaseSession.DepartmentIds.ShouldBe([SeedOrganisation.Departments.InformationSystems]);
        ping.DatabaseSession.Roles.ShouldBe(SeedOrganisation.Thomas.Roles, ignoreOrder: true);
    }

    [Fact]
    public async Task Each_caller_gets_their_own_session_on_a_pooled_connection()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        var client = factory.CreateClient();

        factory.AsUser(SeedOrganisation.Camille);
        var first = await client.GetFromJsonAsync<PingResponse>("/api/ping", TestContext.Current.CancellationToken);

        factory.AsUser(SeedOrganisation.Sofia);
        var second = await client.GetFromJsonAsync<PingResponse>("/api/ping", TestContext.Current.CancellationToken);

        // Postgres session GUCs survive a connection returning to the pool. The interceptor re-stamps on every
        // open specifically so the next request cannot inherit the previous caller's scope — if that ever broke,
        // Sofia (Finance) would be reading with Camille's IS department scope.
        first!.DatabaseSession.UserId.ShouldBe(SeedOrganisation.Camille.UserId);
        second!.DatabaseSession.UserId.ShouldBe(SeedOrganisation.Sofia.UserId);
        second.DatabaseSession.DepartmentIds.ShouldBe([SeedOrganisation.Departments.Finance]);
    }

    [Fact]
    public async Task The_health_endpoint_needs_no_session()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsAnonymous();

        var response = await factory.CreateClient().GetAsync("/alive", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_are_scrapable_without_a_session()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsAnonymous();

        var response = await factory.CreateClient().GetAsync("/metrics", TestContext.Current.CancellationToken);

        // Prometheus scrapes from inside the network and cannot present a user session; if this ever starts
        // returning 401 the dashboards go blank silently.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
