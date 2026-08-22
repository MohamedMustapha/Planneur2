using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// A person's own display preferences (v2 §02) end to end, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The claim these preferences make is a durability one: they are the person's answer, and the directory sync —
/// which rewrites <c>time_zone</c> and <c>ui_language</c> from Keycloak on every run — never touches them. That is
/// not provable from either half alone, which is why the interesting test here runs a real sync over a real row
/// and then reads the preference back.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PreferencesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_synced_person_starts_with_no_preferences_of_their_own()
    {
        await using var factory = await SeededAsync();

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        // Null rather than a copy of the synced value: the client has to be able to tell "she chose French" from
        // "LDAP said French", because only the first survives a change in LDAP.
        me.PreferredLanguage.ShouldBeNull();
        me.PreferredTimeZone.ShouldBeNull();
        me.PreferredTheme.ShouldBeNull();
        me.FocusMode.ShouldBeNull();

        me.UiLanguage.ShouldBe("fr");
        me.TimeZone.ShouldBe("Europe/Paris");
    }

    [Fact]
    public async Task Saving_preferences_hands_back_the_refreshed_record()
    {
        await using var factory = await SeededAsync();

        var saved = await SaveAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest("en", "Europe/Madrid", "dark", FocusMode: true));

        // The whole record comes back rather than a 204, because what changed is the client's context: a save
        // followed by a mandatory re-read is two round trips to learn something the server already knew.
        saved.PreferredLanguage.ShouldBe("en");
        saved.PreferredTimeZone.ShouldBe("Europe/Madrid");
        saved.PreferredTheme.ShouldBe("dark");
        saved.FocusMode.ShouldBe(true);
        saved.PersonId.ShouldBe(SeedOrganisation.Camille.UserId);
    }

    [Fact]
    public async Task A_saved_preference_is_still_there_on_the_next_read()
    {
        await using var factory = await SeededAsync();

        await SaveAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest("es", "America/Montreal", "light", FocusMode: false));

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        me.PreferredLanguage.ShouldBe("es");
        me.PreferredTimeZone.ShouldBe("America/Montreal");
        me.PreferredTheme.ShouldBe("light");

        // False is a decision, not an absence. Someone who turned Focus mode off must not meet it again on the
        // machine in the meeting room because the server read their "no" as "never said".
        me.FocusMode.ShouldBe(false);
    }

    [Fact]
    public async Task A_preference_survives_the_next_directory_sync()
    {
        await using var factory = await SeededAsync();

        await SaveAsync(factory, SeedOrganisation.Camille, new UpdatePreferencesRequest("en", null, "dark", true));

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        var me = await MeAsync(factory, SeedOrganisation.Camille);

        // The whole point of storing these beside the synced columns rather than in them. Sync rewrites
        // ui_language from LDAP on every run; a preference living in that column would be reverted nightly.
        me.PreferredLanguage.ShouldBe("en");
        me.PreferredTheme.ShouldBe("dark");
        me.FocusMode.ShouldBe(true);
        me.UiLanguage.ShouldBe("fr");
    }

    [Fact]
    public async Task Saving_nulls_hands_the_choice_back_to_the_directory()
    {
        await using var factory = await SeededAsync();

        await SaveAsync(factory, SeedOrganisation.Camille, new UpdatePreferencesRequest("en", "UTC", "dark", true));

        var cleared = await SaveAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest(null, null, null, null));

        // Null clears rather than skips: "use whatever the directory says" is a choice somebody may want to make
        // again, and a partial-update shape would leave them no way to express it.
        cleared.PreferredLanguage.ShouldBeNull();
        cleared.PreferredTimeZone.ShouldBeNull();
        cleared.PreferredTheme.ShouldBeNull();
        cleared.FocusMode.ShouldBeNull();
    }

    [Theory]
    [InlineData("en-GB", "en")]
    [InlineData("FR", "fr")]
    [InlineData("es_ES", "es")]
    public async Task A_language_tag_is_reduced_to_the_language(string sent, string stored)
    {
        await using var factory = await SeededAsync();

        var saved = await SaveAsync(factory, SeedOrganisation.Camille, new UpdatePreferencesRequest(sent, null, null));

        saved.PreferredLanguage.ShouldBe(stored);
    }

    [Fact]
    public async Task A_language_the_platform_does_not_speak_is_refused()
    {
        await using var factory = await SeededAsync();

        // Refused rather than quietly normalized to French, which is what the synced attribute does. Storing "fr"
        // for a request that said "de" would look to the person exactly like the setting being ignored.
        var response = await PutAsync(factory, SeedOrganisation.Camille, new UpdatePreferencesRequest("de", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await MeAsync(factory, SeedOrganisation.Camille)).PreferredLanguage.ShouldBeNull();
    }

    [Fact]
    public async Task A_time_zone_this_system_does_not_know_is_refused()
    {
        await using var factory = await SeededAsync();

        var response = await PutAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest(null, "Mars/Olympus_Mons", null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_real_time_zone_is_accepted_whatever_the_host_runs_on()
    {
        await using var factory = await SeededAsync();

        // Asked of the platform rather than checked against a list we maintain, so this is also the assertion that
        // the zone database is reachable from the API container at all.
        var saved = await SaveAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest(null, "Asia/Tokyo", null));

        saved.PreferredTimeZone.ShouldBe("Asia/Tokyo");
    }

    [Fact]
    public async Task A_theme_that_is_not_a_theme_is_refused()
    {
        await using var factory = await SeededAsync();

        var response = await PutAsync(
            factory,
            SeedOrganisation.Camille,
            new UpdatePreferencesRequest(null, null, "solarized"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Preferences_are_one_persons_own_and_not_their_neighbours()
    {
        await using var factory = await SeededAsync();

        await SaveAsync(factory, SeedOrganisation.Camille, new UpdatePreferencesRequest("en", null, "dark", true));

        var mehdi = await MeAsync(factory, SeedOrganisation.Mehdi);

        // There is no person id in the route or the body, so this is less a permission test than a proof that the
        // caller's own identity is what scopes the write — Camille and Mehdi share a unit and see each other.
        mehdi.PreferredLanguage.ShouldBeNull();
        mehdi.PreferredTheme.ShouldBeNull();
        mehdi.FocusMode.ShouldBeNull();
    }

    [Fact]
    public async Task Focus_mode_can_be_toggled_back_and_forth()
    {
        await using var factory = await SeededAsync();

        (await SaveAsync(factory, SeedOrganisation.Thomas, new UpdatePreferencesRequest(null, null, null, true)))
            .FocusMode.ShouldBe(true);

        (await SaveAsync(factory, SeedOrganisation.Thomas, new UpdatePreferencesRequest(null, null, null, false)))
            .FocusMode.ShouldBe(false);

        (await SaveAsync(factory, SeedOrganisation.Thomas, new UpdatePreferencesRequest(null, null, null, null)))
            .FocusMode.ShouldBeNull();
    }

    // --- Fixture -----------------------------------------------------------------------------------------------

    private static async Task<MeResponse> MeAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<MeResponse>(
            "/api/directory/me",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<MeResponse> SaveAsync(
        CracraApplicationFactory factory,
        UserContext person,
        UpdatePreferencesRequest request)
    {
        var response = await PutAsync(factory, person, request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpResponseMessage> PutAsync(
        CracraApplicationFactory factory,
        UserContext person,
        UpdatePreferencesRequest request)
    {
        factory.AsUser(person);

        return await factory.CreateClient().PutAsJsonAsync(
            "/api/directory/me/preferences",
            request,
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
    /// Clears the people this suite writes to. The container is shared, so a preference left behind by one
    /// scenario would satisfy the "starts with nothing" assertion of the next one by accident.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);
    }
}
