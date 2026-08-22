using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// A department's working day (v2 §02), from the settings screen to the week it draws.
/// </summary>
/// <remarks>
/// The shape of a day is department policy, not a platform constant, and the rule that matters is stated twice on
/// purpose: the settings screen refuses an incoherent day, and S5 ignores one that got stored before the rule
/// existed. Only a real stack can show both halves agreeing — that a saved day reaches the weekly summary, and
/// that a bad row already in the table does not take somebody's board offline.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class WorkingDayConfigTests(PostgresFixture postgres)
{
    private const string HelpdeskDay =
        """
        {
          "dayStart": "07:00",
          "dayEnd": "16:00",
          "morning": { "start": "07:30", "end": "12:00" },
          "afternoon": { "start": "12:30", "end": "15:30" }
        }
        """;

    [Fact]
    public async Task A_department_that_configured_nothing_gets_the_platform_day()
    {
        await using var factory = await SeededAsync();

        var day = (await SummaryAsync(factory, SeedOrganisation.Camille)).WorkingDay;

        // Sent rather than left null, so the client never has to carry its own copy of the default — two places
        // deciding what an unconfigured day looks like is two places to disagree.
        day.ShouldNotBeNull();
        day.DayStart.ShouldBe(new TimeOnly(6, 0));
        day.DayEnd.ShouldBe(new TimeOnly(20, 0));
        day.MorningStart.ShouldBe(new TimeOnly(9, 0));
        day.AfternoonEnd.ShouldBe(new TimeOnly(18, 0));
    }

    [Fact]
    public async Task A_saved_working_day_comes_back_from_the_settings_screen()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, HelpdeskDay);

        var config = await ConfigAsync(factory, SeedOrganisation.Olivier);

        // The blob travels back out. It did not always: a snapshot falling back to its own "{}" default would let
        // the editor read an empty day for a department that had configured one, and write that back on save.
        config.WorkingDayJson.ShouldContain("07:00");
    }

    [Fact]
    public async Task A_saved_working_day_reaches_the_week_it_draws()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, HelpdeskDay);

        var day = (await SummaryAsync(factory, SeedOrganisation.Camille)).WorkingDay;

        // Travels with the weekly summary rather than through an endpoint of its own: it answers the same
        // question that payload already answers, and every board would otherwise fetch both together anyway.
        day.ShouldNotBeNull();
        day.DayStart.ShouldBe(new TimeOnly(7, 0));
        day.DayEnd.ShouldBe(new TimeOnly(16, 0));
        day.MorningEnd.ShouldBe(new TimeOnly(12, 0));
        day.AfternoonStart.ShouldBe(new TimeOnly(12, 30));
    }

    [Fact]
    public async Task One_departments_hours_are_not_another_departments()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, HelpdeskDay);

        // Sofia is in Finance, which configured nothing. A helpdesk on 07:00-15:00 and an accounting team on the
        // ordinary office day are both normal, and neither should be drawn on the other's axis.
        var day = (await SummaryAsync(factory, SeedOrganisation.Sofia)).WorkingDay;

        day.ShouldNotBeNull();
        day.DayStart.ShouldBe(new TimeOnly(6, 0));
    }

    [Fact]
    public async Task A_day_with_a_session_outside_it_is_refused_at_the_door()
    {
        await using var factory = await SeededAsync();

        var response = await PutConfigAsync(
            factory,
            """
            {
              "dayStart": "09:00",
              "dayEnd": "17:00",
              "morning": { "start": "09:00", "end": "13:00" },
              "afternoon": { "start": "14:00", "end": "19:00" }
            }
            """);

        // Everything parses; together they describe an afternoon outside the day it belongs to. S5 would ignore
        // such a row and fall back, which means a save accepted here would look clean and then quietly do nothing.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_refused_day_leaves_the_stored_configuration_untouched()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, HelpdeskDay);

        var before = await ConfigAsync(factory, SeedOrganisation.Olivier);

        await PutConfigAsync(factory, """{"dayStart":"nine o'clock"}""");

        var after = await ConfigAsync(factory, SeedOrganisation.Olivier);

        // Validation runs before anything is written, so a rejected edit costs neither the previous day nor a
        // version bump — the audit trail would otherwise record a change that never happened.
        after.Version.ShouldBe(before.Version);
        after.WorkingDayJson.ShouldContain("07:00");
    }

    [Fact]
    public async Task An_empty_object_is_how_a_department_says_use_the_defaults()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, HelpdeskDay);
        await ConfigureAsync(factory, "{}");

        var day = (await SummaryAsync(factory, SeedOrganisation.Camille)).WorkingDay;

        day.ShouldNotBeNull();
        day.DayStart.ShouldBe(new TimeOnly(6, 0));
    }

    [Fact]
    public async Task A_row_that_predates_the_rule_falls_back_rather_than_breaking_the_week()
    {
        await using var factory = await SeededAsync();

        // Written past the settings screen, which is the only way this shape can exist: a row stored before the
        // validator learned to refuse it. The domain check exists a second time for exactly this case, and a
        // board that refused to draw because of a settings blob would be a worse failure than a default one.
        await StoreWorkingDayDirectlyAsync(
            factory,
            """{"dayStart":"09:00","dayEnd":"08:00","morning":{"start":"09:00","end":"10:00"},"afternoon":{"start":"10:00","end":"11:00"}}""");

        var summary = await SummaryAsync(factory, SeedOrganisation.Camille);

        summary.WorkingDay.ShouldNotBeNull();
        summary.WorkingDay.DayStart.ShouldBe(new TimeOnly(6, 0));
        summary.WorkingDay.DayEnd.ShouldBe(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task A_partial_row_keeps_the_defaults_it_did_not_state()
    {
        await using var factory = await SeededAsync();

        await StoreWorkingDayDirectlyAsync(factory, """{"dayStart":"07:00"}""");

        var day = (await SummaryAsync(factory, SeedOrganisation.Camille)).WorkingDay;

        // A department that only wants a later start should not have to restate the other five values — and
        // should certainly not lose them.
        day.ShouldNotBeNull();
        day.DayStart.ShouldBe(new TimeOnly(7, 0));
        day.DayEnd.ShouldBe(new TimeOnly(20, 0));
        day.MorningStart.ShouldBe(new TimeOnly(9, 0));
    }

    // --- Fixture -----------------------------------------------------------------------------------------------

    private static async Task<WeeklySummary> SummaryAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<WeeklySummary>(
            "/api/activities/weekly-summary/me?week=2026-W34",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<DepartmentConfigSnapshot> ConfigAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<DepartmentConfigSnapshot>(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task ConfigureAsync(CracraApplicationFactory factory, string workingDayJson) =>
        (await PutConfigAsync(factory, workingDayJson)).StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>Rewrites the IS department's configuration as its head, which is who is allowed to.</summary>
    private static async Task<HttpResponseMessage> PutConfigAsync(
        CracraApplicationFactory factory,
        string workingDayJson)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        return await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            new
            {
                activityTaxonomyJson = "{}",
                roleLabelsJson = "{}",
                kudoRulesJson = "{}",
                defaultBoardLayout = "week",
                iterationPresetsJson = """["1w","2w","1m"]""",
                weeklyTargetHours = 35m,
                enforceWeeklyTarget = false,
                shiftTemplatesJson = "{}",
                workingDayJson,
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Past the settings screen and straight into the table, as a row written before the rule existed.</summary>
    private static async Task StoreWorkingDayDirectlyAsync(CracraApplicationFactory factory, string workingDayJson)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.DepartmentConfigs
            .Where(config => config.DepartmentId == SeedOrganisation.Departments.InformationSystems)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(config => config.WorkingDayJson, workingDayJson),
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

    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);

        // Reset rather than delete: sync creates a config only alongside its department, and the departments
        // survive this. The working day is the one this suite writes, so it is the one that has to be cleared.
        await directory.DepartmentConfigs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(config => config.WorkingDayJson, "{}")
                .SetProperty(config => config.ActivityTaxonomyJson, "{}")
                .SetProperty(config => config.WeeklyTargetHours, 35m)
                .SetProperty(config => config.EnforceWeeklyTarget, false),
            ct);
    }
}
