using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// Node profiles end to end (v2 §10), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// <para>
/// The claim the slice makes is that two sibling units under one department can behave differently without either
/// being a different <em>kind</em> of thing in the schema. That is only provable against real rows: the unit
/// tests prove the walk, and these prove that the walk is wired to the things people actually meet — the
/// vocabulary their activity picker offers, and the controls their branch does or does not get.
/// </para>
/// <para>
/// Infrastructure and Development are the pair throughout. They share a department, which is what makes the
/// assertions mean something — anything that distinguishes them here cannot be coming from the department.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class NodeProfileTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_shipped_example_profiles_are_there_to_be_attached()
    {
        // Seeded as data, not classes (§10.2). An administrator may delete all three, so nothing asserts that
        // these specific codes exist forever — only that a fresh database arrives with something to clone.
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var profiles = await factory.CreateClient().GetFromJsonAsync<List<NodeProfileDetail>>(
            "/api/directory/profiles",
            TestContext.Current.CancellationToken);

        profiles.ShouldNotBeNull();
        profiles.Select(profile => profile.Code).ShouldContain("DELIVERY");
        profiles.Select(profile => profile.Code).ShouldContain("DISPATCH");
    }

    [Fact]
    public async Task A_person_with_no_profile_anywhere_above_them_gets_none()
    {
        // And must therefore lose nothing. This is the case that keeps the slice adoptable: a deployment that has
        // authored nothing still has every control it had yesterday.
        await using var factory = await SeededAsync();

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        me.Profile.ShouldBeNull();
    }

    [Fact]
    public async Task The_profile_attached_to_a_unit_reaches_the_person_who_works_in_it()
    {
        await using var factory = await SeededAsync();

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "DISPATCH");

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        me.Profile.ShouldNotBeNull();
        me.Profile.SourceCode.ShouldBe("DISPATCH");
        me.Profile.BoardArchetypes.ShouldContain("work-orders");
    }

    [Fact]
    public async Task Two_units_of_one_department_get_different_answers()
    {
        // The whole slice, in one assertion. Camille and Olivier are in the same department; nothing about their
        // department differs; and they get different boards and different capabilities.
        await using var factory = await SeededAsync();

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "DISPATCH");
        await AttachAsync(factory, SeedOrganisation.Units.Development, "DELIVERY");

        var dispatch = await MeAsync(factory, SeedOrganisation.Camille);
        var delivery = await MeAsync(factory, SeedOrganisation.Olivier);

        dispatch.Profile!.BoardArchetypes.ShouldContain("work-orders");
        delivery.Profile!.BoardArchetypes.ShouldContain("task-progress");

        dispatch.Profile.Capabilities[NodeCapabilities.WorkOrderPool].ShouldBeTrue();
        delivery.Profile.Capabilities[NodeCapabilities.WorkOrderPool].ShouldBeFalse();

        dispatch.Profile.Capabilities[NodeCapabilities.Budget].ShouldBeFalse();
        delivery.Profile.Capabilities[NodeCapabilities.Budget].ShouldBeTrue();
    }

    [Fact]
    public async Task A_department_profile_reaches_a_unit_that_attached_none()
    {
        // Inheritance, over HTTP. The unit says nothing; the answer comes from above it.
        await using var factory = await SeededAsync();

        await AttachDepartmentAsync(factory, SeedOrganisation.Departments.InformationSystems, "ADVISORY");

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        me.Profile.ShouldNotBeNull();
        me.Profile.SourceCode.ShouldBe("ADVISORY");
        me.Profile.Capabilities[NodeCapabilities.Integrations].ShouldBeFalse();
    }

    [Fact]
    public async Task A_unit_overrides_its_department_without_restating_it()
    {
        await using var factory = await SeededAsync();

        await AttachDepartmentAsync(factory, SeedOrganisation.Departments.InformationSystems, "ADVISORY");

        // A profile that names only a board. Everything else must still come from the department's ADVISORY.
        var boardOnly = await CreateAsync(
            factory,
            new SaveNodeProfileRequest("BOARD-ONLY", "profile.board-only", BoardArchetypes: ["work-orders"]));

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, boardOnly.Id);

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        me.Profile.ShouldNotBeNull();
        me.Profile.SourceCode.ShouldBe("BOARD-ONLY");
        me.Profile.BoardArchetypes.ShouldBe(["work-orders"]);

        // Inherited, not copied. If the department later switches integrations back on, this unit follows.
        me.Profile.Capabilities[NodeCapabilities.Integrations].ShouldBeFalse();
    }

    [Fact]
    public async Task Detaching_a_unit_returns_it_to_what_it_inherits()
    {
        await using var factory = await SeededAsync();

        await AttachDepartmentAsync(factory, SeedOrganisation.Departments.InformationSystems, "ADVISORY");
        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "DISPATCH");

        (await MeAsync(factory, SeedOrganisation.Camille)).Profile!.SourceCode.ShouldBe("DISPATCH");

        // Null is a real edit, not a no-op: the unit goes back to asking upward.
        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, profileId: null);

        (await MeAsync(factory, SeedOrganisation.Camille)).Profile!.SourceCode.ShouldBe("ADVISORY");
    }

    // --- Taxonomy ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_activity_picker_offers_the_branchs_own_subtypes()
    {
        await using var factory = await SeededAsync();

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "DISPATCH");

        var types = await ActivityTypesAsync(factory, SeedOrganisation.Camille);

        // Its own vocabulary...
        types.ShouldContain("triage");
        types.ShouldContain("intervention");

        // ...and the four buckets, which are the platform's and are never a branch's to remove (§10.2). Without
        // them an L1 head's rollup could not stack dissimilar branches under common headings.
        types.ShouldContain("project-build");
        types.ShouldContain("project-run");
        types.ShouldContain("quality-of-life");
        types.ShouldContain("recruitment-admin");
    }

    [Fact]
    public async Task A_branch_is_not_offered_its_siblings_subtypes()
    {
        await using var factory = await SeededAsync();

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "DISPATCH");
        await AttachAsync(factory, SeedOrganisation.Units.Development, "DELIVERY");

        var dispatch = await ActivityTypesAsync(factory, SeedOrganisation.Camille);
        var delivery = await ActivityTypesAsync(factory, SeedOrganisation.Olivier);

        dispatch.ShouldContain("triage");
        dispatch.ShouldNotContain("architecture");

        delivery.ShouldContain("architecture");
        delivery.ShouldNotContain("triage");
    }

    [Fact]
    public async Task Logging_a_subtype_the_resolved_taxonomy_does_not_offer_is_rejected()
    {
        await using var factory = await SeededAsync();

        // ADVISORY rather than DISPATCH for this pair, because its subtypes sit under quality-of-life and so need
        // no project. That keeps both halves about the taxonomy: a subtype under project-build or project-run
        // inherits the bucket's project requirement, and a rejection for the wrong reason would prove nothing.
        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "ADVISORY");

        // "architecture" is a real subtype — of another branch's profile. That is what makes this the right
        // negative case: it is not a typo, it is somebody else's vocabulary, and the rejection has to come from
        // the resolved taxonomy rather than from a global list of known codes.
        var response = await LogAsync(factory, SeedOrganisation.Camille, "architecture");

        // 422, the same status any other domain rule violation gets. Worth pinning rather than accepting "not
        // 2xx": a 404 here would mean the endpoint never resolved the taxonomy at all, which would pass a laxer
        // assertion while proving the opposite of what this test claims.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        problem.ShouldContain("architecture");
    }

    [Fact]
    public async Task Logging_the_branchs_own_subtype_is_accepted()
    {
        // The positive half. Without it the test above would pass just as well against a taxonomy that rejected
        // everything.
        await using var factory = await SeededAsync();

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, "ADVISORY");

        var response = await LogAsync(factory, SeedOrganisation.Camille, "research");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // --- Authoring ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_administrator_can_author_a_profile_with_invented_vocabulary()
    {
        // §10's configurability claim: no deployment, no migration, no code that has heard of these words.
        await using var factory = await SeededAsync();

        var authored = await CreateAsync(
            factory,
            new SaveNodeProfileRequest(
                "CASEWORK",
                "profile.casework",
                ActivityTaxonomyJson: """
                    {"types":[
                      {"code":"eligibility-check","parent":"project-run"},
                      {"code":"payment-run","parent":"project-run"}
                    ]}
                    """,
                BoardArchetypes: ["week-grid"],
                CapabilitiesJson: """{"integrations":false}"""));

        await AttachAsync(factory, SeedOrganisation.Units.Infrastructure, authored.Id);

        var types = await ActivityTypesAsync(factory, SeedOrganisation.Camille);

        types.ShouldContain("eligibility-check");
        types.ShouldContain("payment-run");

        (await MeAsync(factory, SeedOrganisation.Camille))
            .Profile!.Capabilities[NodeCapabilities.Integrations].ShouldBeFalse();
    }

    [Fact]
    public async Task Cloning_copies_the_nulls_as_nulls()
    {
        await using var factory = await SeededAsync();

        var source = await CreateAsync(
            factory,
            new SaveNodeProfileRequest("BASE", "profile.base", BoardArchetypes: ["work-orders"]));

        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/directory/profiles/{source.Id}/clone",
            new { code = "BASE-2", labelKey = "profile.base" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var clone = await response.Content.ReadFromJsonAsync<NodeProfileDetail>(
            TestContext.Current.CancellationToken);

        clone.ShouldNotBeNull();
        clone.BoardArchetypes.ShouldBe(["work-orders"]);

        // The important half: a clone of a profile that inherits must also inherit, not freeze today's answer.
        clone.ActivityTaxonomyJson.ShouldBeNull();
        clone.CapabilitiesJson.ShouldBeNull();
    }

    [Fact]
    public async Task A_member_cannot_author_a_profile()
    {
        // Profiles are shared vocabulary — editing one changes behaviour for every branch pointing at it — so
        // authoring stays with the PMO even though attaching does not.
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/directory/profiles",
            new SaveNodeProfileRequest("SNEAKY", "profile.sneaky"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_cannot_attach_a_profile_to_their_unit()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var profiles = await ProfilesAsync(factory);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/units/{SeedOrganisation.Units.Infrastructure}/profile",
            new { profileId = profiles.First().Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_capability_registry_is_served_rather_than_hardcoded_in_the_admin_screen()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var capabilities = await factory.CreateClient().GetFromJsonAsync<Dictionary<string, bool>>(
            "/api/directory/profiles/capabilities",
            TestContext.Current.CancellationToken);

        capabilities.ShouldNotBeNull();
        capabilities.Keys.ShouldBe(NodeCapabilities.All, ignoreOrder: true);

        // Permissive defaults, asserted because they are what makes this slice safe to ship into a deployment
        // that has configured nothing.
        capabilities.Values.ShouldAllBe(enabled => enabled);
    }

    // --- Fixture -----------------------------------------------------------------------------------------------

    private static async Task<MeResponse> MeAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<MeResponse>(
            "/api/directory/me",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<NodeProfileDetail>> ProfilesAsync(CracraApplicationFactory factory) =>
        (await factory.CreateClient().GetFromJsonAsync<List<NodeProfileDetail>>(
            "/api/directory/profiles",
            TestContext.Current.CancellationToken))!;

    private static async Task<NodeProfileDetail> CreateAsync(
        CracraApplicationFactory factory,
        SaveNodeProfileRequest request)
    {
        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/directory/profiles",
            request,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<NodeProfileDetail>(
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Attaches by code, so a test reads as "this unit runs dispatch" rather than as a guid.</summary>
    private static async Task AttachAsync(CracraApplicationFactory factory, Guid unitId, string code)
    {
        factory.AsUser(SeedOrganisation.Nadia);

        var profiles = await ProfilesAsync(factory);

        await AttachAsync(factory, unitId, profiles.Single(profile => profile.Code == code).Id);
    }

    private static async Task AttachAsync(CracraApplicationFactory factory, Guid unitId, Guid? profileId)
    {
        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/units/{unitId}/profile",
            new { profileId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AttachDepartmentAsync(
        CracraApplicationFactory factory,
        Guid departmentId,
        string code)
    {
        factory.AsUser(SeedOrganisation.Nadia);

        var profiles = await ProfilesAsync(factory);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{departmentId}/profile",
            new { profileId = profiles.Single(profile => profile.Code == code).Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<IReadOnlyList<string>> ActivityTypesAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        var options = await factory.CreateClient().GetFromJsonAsync<List<ActivityTypeOption>>(
            "/api/activities/types",
            TestContext.Current.CancellationToken);

        return [.. options!.Select(option => option.Code)];
    }

    private static async Task<HttpResponseMessage> LogAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string activityTypeCode)
    {
        factory.AsUser(person);

        // The same Monday the activities suite uses. A date inside a normal working week keeps this test about
        // the taxonomy rather than about the working-day rules, which have their own suite.
        var day = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        return await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode,
                kind = "actual",
                source = "manual",
                slotStart = day,
                slotEnd = day.AddHours(2),
                hours = 2m,
            },
            TestContext.Current.CancellationToken);
    }

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    /// <summary>
    /// Detaches every node and drops profiles this suite authored.
    /// </summary>
    /// <remarks>
    /// The container is shared, so an attachment left behind by one scenario would satisfy the next one's
    /// "starts with nothing" assertion by accident. The three seeded profiles stay — they arrive with the
    /// migration, and deleting them would make every later run test a database no deployment ever has.
    /// </remarks>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;
        var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        await directory.Units.ExecuteUpdateAsync(unit => unit.SetProperty(u => u.ProfileId, (Guid?)null), ct);
        await directory.Departments.ExecuteUpdateAsync(
            department => department.SetProperty(d => d.ProfileId, (Guid?)null),
            ct);

        await directory.NodeProfiles
            .Where(profile => profile.ModifiedBy != "seed")
            .ExecuteDeleteAsync(ct);

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);
    }
}
