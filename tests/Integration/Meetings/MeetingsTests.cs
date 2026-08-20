using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Services;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Meetings;

/// <summary>
/// S7 through HTTP, with real RLS: series and special days, their targeting, and what they do to a board.
/// </summary>
/// <remarks>
/// This is a 2-layer module, so per conventions.md §6 there is little worth unit-testing beyond the recurrence
/// engine and everything else is covered here, end to end. The assertions worth having are the ones that would
/// catch the module's central claim being false: that a scope pair decides who sees a meeting, who may schedule
/// one, and what lands on which board.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MeetingsTests(PostgresFixture postgres)
{
    private static readonly DateOnly Monday = new(2026, 8, 17);
    private static readonly DateOnly Friday = new(2026, 8, 21);

    // --- Series --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_unit_head_schedules_a_weekly_stand_up_and_its_occurrences_are_materialized()
    {
        await using var factory = await SeededAsync();

        var series = await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        series.Kind.ShouldBe(MeetingKinds.Weekly);
        series.ScopeType.ShouldBe(MeetingScopeTypes.Unit);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Monday.AddDays(20));

        // Three Mondays in a three-week window. Occurrences are rows, written when the series is written, so a
        // board reading a window runs one range scan rather than expanding every rule in the organization.
        occurrences.Count.ShouldBe(3);
        occurrences.ShouldAllBe(occurrence => occurrence.SeriesId == series.Id);
        occurrences[0].StartsAt.ShouldBe(new DateTimeOffset(2026, 8, 17, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task The_recurrence_rule_is_stored_canonically()
    {
        await using var factory = await SeededAsync();

        var series = await CreateSeriesAsync(
            factory,
            SeedOrganisation.Thomas,
            rule: "RRULE:FREQ=WEEKLY;INTERVAL=1;BYDAY=MO");

        // INTERVAL=1 is the default and the prefix is part of the iCal line, not of the rule. Two people writing
        // the same rule two ways must produce one string, or every consumer starts comparing them by parsing.
        series.RecurrenceRule.ShouldBe("FREQ=WEEKLY;BYDAY=MO");
    }

    [Fact]
    public async Task A_rule_the_parser_does_not_support_is_refused_with_the_reason()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/series",
            SeriesPayload(rule: "FREQ=WEEKLY;BYSETPOS=-1"),
            TestContext.Current.CancellationToken);

        // 422, not a silently truncated rule. A schedule that is confidently wrong is the one outcome worth
        // refusing outright, because nobody goes looking for it.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_member_cannot_schedule_anything()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/series",
            SeriesPayload(),
            TestContext.Current.CancellationToken);

        // The endpoint policy is the door: scheduling is a scope owner's act, and Camille is a plain member.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_of_another_department_cannot_schedule_into_this_one()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Laurent);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/series",
            SeriesPayload(scopeType: MeetingScopeTypes.Department,
                scopeId: SeedOrganisation.Departments.InformationSystems),
            TestContext.Current.CancellationToken);

        // Laurent passes the policy — he is a department head — and is stopped by the write predicate, which is
        // the only layer that knows *which* department. 404 rather than 403 because the resolver could not even
        // find the department for him: RLS removed it before the write was attempted.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_PMO_may_schedule_something_org_wide()
    {
        await using var factory = await SeededAsync();

        var refused = await PostSeriesAsync(
            factory, SeedOrganisation.Olivier, SeriesPayload(scopeType: MeetingScopeTypes.Org, scopeId: null));

        // A department head reaching for "everybody" is exactly the escalation the write predicate exists to stop.
        // It is the policy refusing the row, so it surfaces as a 403 rather than as a 404.
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await PostSeriesAsync(
            factory, SeedOrganisation.Nadia, SeriesPayload(scopeType: MeetingScopeTypes.Org, scopeId: null));

        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Moving_a_series_rewrites_its_future_and_leaves_its_past_alone()
    {
        await using var factory = await SeededAsync();

        var series = await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/meetings/series/{series.Id}",
            SeriesPayload(rule: "FREQ=WEEKLY;BYDAY=TU"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Today(), Today().AddDays(28));

        // Every future instance now falls on a Tuesday. Nothing before today moved: somebody rescheduling the
        // stand-up is not claiming it was always on a Tuesday, and the notes on past ones stay where they are.
        occurrences.ShouldNotBeEmpty();
        occurrences.ShouldAllBe(occurrence => occurrence.StartsAt.UtcDateTime.DayOfWeek == DayOfWeek.Tuesday);
    }

    [Fact]
    public async Task Deactivating_a_series_clears_its_future()
    {
        await using var factory = await SeededAsync();

        var series = await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        factory.AsUser(SeedOrganisation.Thomas);

        await factory.CreateClient().PutAsJsonAsync(
            $"/api/meetings/series/{series.Id}",
            SeriesPayload(active: false),
            TestContext.Current.CancellationToken);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Today(), Today().AddDays(60));

        occurrences.ShouldBeEmpty();

        // Deactivated, not deleted — a copil that ran for two years and stopped is history somebody asks about.
        var series_ = await SeriesAsync(factory, SeedOrganisation.Thomas, includeInactive: true);

        series_.ShouldContain(candidate => candidate.Id == series.Id && !candidate.Active);
    }

    // --- Targeting and RLS ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_member_sees_a_meeting_aimed_at_their_own_unit()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6));

        // Camille is in Infrastructure, which is what the series targets. This is the matrix's "ones targeting
        // them/unit" for a plain member.
        occurrences.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_member_does_not_see_another_units_meeting()
    {
        await using var factory = await SeededAsync();

        // Aimed at Development, where Camille is not.
        await CreateSeriesAsync(
            factory,
            SeedOrganisation.Olivier,
            scopeId: SeedOrganisation.Units.Development);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6));

        occurrences.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_department_head_sees_every_unit_in_their_department()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Olivier, Monday, Monday.AddDays(6));

        // Olivier heads IS but sits in Development. He reaches Infrastructure's stand-up through the denormalized
        // department column, which is why the row carries it — so the predicate never reads directory.unit.
        occurrences.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_head_of_another_department_sees_none_of_it()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var occurrences = await OccurrencesAsync(factory, SeedOrganisation.Laurent, Monday, Monday.AddDays(6));

        occurrences.ShouldBeEmpty();
    }

    [Fact]
    public async Task Everybody_sees_an_org_wide_series()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(
            factory,
            SeedOrganisation.Nadia,
            scopeType: MeetingScopeTypes.Org,
            scopeId: null);

        foreach (var person in new[] { SeedOrganisation.Camille, SeedOrganisation.Sofia, SeedOrganisation.Laurent })
        {
            (await OccurrencesAsync(factory, person, Monday, Monday.AddDays(6))).ShouldHaveSingleItem();
        }
    }

    // --- Special days --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_department_head_adds_a_patch_party_and_every_member_of_the_department_sees_it()
    {
        await using var factory = await SeededAsync();

        await CreateSpecialDayAsync(factory, SeedOrganisation.Olivier);

        var camille = await SpecialDaysAsync(factory, SeedOrganisation.Camille);

        // The point of the department scope: a patch party has to reach every member, not only heads. A
        // department-wide audit day nobody can see is a day nobody prepares for.
        camille.ShouldHaveSingleItem().Kind.ShouldBe(SpecialDayKinds.PatchParty);
        camille[0].Severity.ShouldBe(SpecialDaySeverities.Warning);
    }

    [Fact]
    public async Task Someone_in_another_department_does_not_see_it()
    {
        await using var factory = await SeededAsync();

        await CreateSpecialDayAsync(factory, SeedOrganisation.Olivier);

        (await SpecialDaysAsync(factory, SeedOrganisation.Sofia)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_cannot_declare_a_special_day()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/special-days",
            SpecialDayPayload(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_severity_outside_the_three_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/special-days",
            SpecialDayPayload(severity: "catastrophic"),
            TestContext.Current.CancellationToken);

        // A closed set, unlike the kinds: severity maps onto three colours the design fixes, and a fourth value
        // would render as nothing at all.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- Windowing -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_occurrence_window_is_inclusive_of_its_last_day()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas, rule: "FREQ=WEEKLY;BYDAY=FR", startsOn: Monday);

        // The Friday meeting is at 09:00 local. A window ending "at" Friday that stopped at midnight would drop
        // it, which is why the window is half-open on the instant and inclusive on the date.
        (await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Friday)).ShouldHaveSingleItem();
        (await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Friday.AddDays(-1))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_returns_only_what_falls_inside_it()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var week = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Monday.AddDays(6));
        var fortnight = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Monday.AddDays(13));

        week.ShouldHaveSingleItem();
        fortnight.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_inverted_window_is_refused_rather_than_answered_with_nothing()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().GetAsync(
            $"/api/meetings/occurrences?from={Monday:yyyy-MM-dd}&to={Monday.AddDays(-7):yyyy-MM-dd}",
            TestContext.Current.CancellationToken);

        // Empty would be indistinguishable from a quiet fortnight, and the caller would never learn their
        // parameters were the wrong way round.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_horizon_sweeper_extends_the_calendar_without_duplicating_it()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var before = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Today(), Today().AddDays(60));

        await factory.Services.GetRequiredService<IMeetingHorizonSweeper>()
            .SweepAsync(TestContext.Current.CancellationToken);

        var after = await OccurrencesAsync(factory, SeedOrganisation.Thomas, Today(), Today().AddDays(60));

        // The sweeper runs daily in production, so an idempotency failure here would double the calendar every
        // night — visibly wrong within a week, and unrecoverable without a manual clean-up.
        after.Count.ShouldBe(before.Count);
    }

    // --- Attendance ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Somebody_answers_a_copil_invitation_and_sees_their_own_answer_back()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas, kind: MeetingKinds.Copil);

        var occurrence = (await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6)))[0];

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/occurrences/{occurrence.Id}/respond",
            new { response = AttendanceResponses.Tentative },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = (await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6)))[0];
        var somebody_else = (await OccurrencesAsync(factory, SeedOrganisation.Mehdi, Monday, Monday.AddDays(6)))[0];

        mine.MyResponse.ShouldBe(AttendanceResponses.Tentative);

        // "My response" means the caller's, and Mehdi has not answered. The person id comes from the session and
        // never from the body, so there is nothing here to answer on somebody else's behalf.
        somebody_else.MyResponse.ShouldBeNull();
    }

    [Fact]
    public async Task Answering_twice_replaces_the_answer_rather_than_adding_one()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas, kind: MeetingKinds.Copil);

        var occurrence = (await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6)))[0];

        await RespondAsync(factory, occurrence.Id, AttendanceResponses.Accepted);
        await RespondAsync(factory, occurrence.Id, AttendanceResponses.Declined);

        var mine = (await OccurrencesAsync(factory, SeedOrganisation.Camille, Monday, Monday.AddDays(6)))[0];

        mine.MyResponse.ShouldBe(AttendanceResponses.Declined);
    }

    [Fact]
    public async Task Somebody_cannot_answer_for_a_meeting_they_cannot_see()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas, kind: MeetingKinds.Copil);

        var occurrence = (await OccurrencesAsync(factory, SeedOrganisation.Thomas, Monday, Monday.AddDays(6)))[0];

        factory.AsUser(SeedOrganisation.Sofia);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/occurrences/{occurrence.Id}/respond",
            new { response = AttendanceResponses.Accepted },
            TestContext.Current.CancellationToken);

        // RLS removed the row before the service ever saw it, so this is genuinely a 404 rather than a refusal —
        // and a prober learns nothing about whether the meeting exists.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Coming up, and the boards -------------------------------------------------------------------------------

    [Fact]
    public async Task The_coming_up_strip_merges_meetings_and_special_days_in_order()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas, rule: "FREQ=DAILY", startsOn: Today());
        await CreateSpecialDayAsync(factory, SeedOrganisation.Olivier, date: Today().AddDays(2));

        factory.AsUser(SeedOrganisation.Camille);

        var upcoming = await factory.CreateClient().GetFromJsonAsync<List<UpcomingEntry>>(
            "/api/meetings/upcoming?days=7",
            TestContext.Current.CancellationToken);

        upcoming.ShouldNotBeNull();
        upcoming.ShouldContain(entry => entry.Kind == SpecialDayKinds.PatchParty);
        upcoming.ShouldContain(entry => entry.Kind == MeetingKinds.Weekly);

        // One ordered list, merged on the server. Two calls and a client-side merge is exactly the assembly work
        // that goes subtly wrong, and the strip sits on the dashboard where a second round trip is felt.
        upcoming.Select(entry => entry.At).ShouldBeInOrder();
    }

    [Fact]
    public async Task A_patch_party_lands_on_the_department_board_as_an_overlay()
    {
        await using var factory = await SeededAsync();

        await CreateSpecialDayAsync(factory, SeedOrganisation.Olivier, date: Monday.AddDays(2));

        var board = await BoardAsync(factory, SeedOrganisation.Olivier, "department");

        // The seam S6 left open, now filled: the board payload and every client template were unchanged, and this
        // is the evidence that the registration swap actually reaches the canvas.
        var overlay = board.Overlays.ShouldHaveSingleItem();

        overlay.Kind.ShouldBe(SpecialDayKinds.PatchParty);
        overlay.From.ShouldBe(Monday.AddDays(2));
        overlay.Color.ShouldBe("var(--event-warning)");
    }

    [Fact]
    public async Task A_stand_up_shows_on_its_own_units_board_and_not_on_a_sibling_units()
    {
        await using var factory = await SeededAsync();

        await CreateSeriesAsync(factory, SeedOrganisation.Thomas);

        var infrastructure = await BoardAsync(factory, SeedOrganisation.Thomas, "team");
        var development = await BoardAsync(factory, SeedOrganisation.Olivier, "team");

        // Olivier heads the department and can *see* Infrastructure's stand-up. Drawing it on Development's board
        // would bury the things that board exists to show — so the overlay source narrows to the board that asked.
        infrastructure.Overlays.ShouldContain(overlay => overlay.Kind == "meeting");
        development.Overlays.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_board_shows_no_overlay_for_a_department_the_caller_is_not_in()
    {
        await using var factory = await SeededAsync();

        await CreateSpecialDayAsync(factory, SeedOrganisation.Olivier);

        var board = await BoardAsync(
            factory,
            SeedOrganisation.Laurent,
            "department",
            SeedOrganisation.Departments.InformationSystems);

        // Asking for another department's board is not refused; it simply contains nothing Laurent may see. The
        // overlays inherit that without restating it, because the reader runs in his own RLS session.
        board.Overlays.ShouldBeEmpty();
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    private static DateOnly Today() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

    private static object SeriesPayload(
        string kind = MeetingKinds.Weekly,
        string scopeType = MeetingScopeTypes.Unit,
        Guid? scopeId = null,
        string rule = "FREQ=WEEKLY;BYDAY=MO",
        DateOnly? startsOn = null,
        bool active = true) => new
        {
            kind,
            nameKey = "meetings.kind.weekly",
            scopeType,
            scopeId = scopeType == MeetingScopeTypes.Org
                ? (Guid?)null
                : scopeId ?? SeedOrganisation.Units.Infrastructure,
            recurrenceRule = rule,
            startsOn = (startsOn ?? Monday).ToString("yyyy-MM-dd"),
            startTime = "09:00:00",
            timeZoneId = "Europe/Paris",
            durationMinutes = 30,
            location = "Salle Ada",
            videoLink = "https://meet.example.invalid/standup",
            active,
        };

    private static object SpecialDayPayload(
        string kind = SpecialDayKinds.PatchParty,
        string scopeType = MeetingScopeTypes.Department,
        Guid? scopeId = null,
        DateOnly? date = null,
        string severity = SpecialDaySeverities.Warning) => new
        {
            kind,
            nameKey = "meetings.specialDay.patchParty",
            scopeType,
            scopeId = scopeId ?? SeedOrganisation.Departments.InformationSystems,
            date = (date ?? Monday.AddDays(2)).ToString("yyyy-MM-dd"),
            allDay = true,
            severity,
            description = "Fenêtre de patch mensuelle.",
        };

    private static async Task<HttpResponseMessage> PostSeriesAsync(
        CracraApplicationFactory factory,
        UserContext person,
        object payload)
    {
        factory.AsUser(person);

        return await factory.CreateClient()
            .PostAsJsonAsync("/api/meetings/series", payload, TestContext.Current.CancellationToken);
    }

    private static async Task<MeetingSeriesView> CreateSeriesAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string kind = MeetingKinds.Weekly,
        string scopeType = MeetingScopeTypes.Unit,
        Guid? scopeId = null,
        string rule = "FREQ=WEEKLY;BYDAY=MO",
        DateOnly? startsOn = null)
    {
        var response = await PostSeriesAsync(
            factory, person, SeriesPayload(kind, scopeType, scopeId, rule, startsOn));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<MeetingSeriesView>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<SpecialDayView> CreateSpecialDayAsync(
        CracraApplicationFactory factory,
        UserContext person,
        DateOnly? date = null)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/special-days",
            SpecialDayPayload(date: date),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<SpecialDayView>(TestContext.Current.CancellationToken))!;
    }

    private static async Task RespondAsync(CracraApplicationFactory factory, Guid occurrenceId, string answer)
    {
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/occurrences/{occurrenceId}/respond",
            new { response = answer },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<List<MeetingOccurrenceView>> OccurrencesAsync(
        CracraApplicationFactory factory,
        UserContext person,
        DateOnly from,
        DateOnly to)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<MeetingOccurrenceView>>(
            $"/api/meetings/occurrences?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<MeetingSeriesView>> SeriesAsync(
        CracraApplicationFactory factory,
        UserContext person,
        bool includeInactive = false)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<MeetingSeriesView>>(
            $"/api/meetings/series?includeInactive={includeInactive}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<SpecialDayView>> SpecialDaysAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<SpecialDayView>>(
            "/api/meetings/special-days",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<BoardPayload> BoardAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        Guid? scopeId = null)
    {
        factory.AsUser(person);

        var scope = scopeId is { } id ? $"&scopeId={id}" : string.Empty;

        return (await factory.CreateClient().GetFromJsonAsync<BoardPayload>(
            $"/api/scheduling/board?type={type}&from=2026-08-17&to=2026-08-23{scope}",
            TestContext.Current.CancellationToken))!;
    }

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

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

        var meetings = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Meetings.Data.MeetingsDbContext>();

        // Series first would do — attendance and occurrences cascade — but naming all three keeps the reset
        // honest about what it clears rather than relying on a foreign key to be right.
        await meetings.Attendance.ExecuteDeleteAsync(ct);
        await meetings.Occurrences.ExecuteDeleteAsync(ct);
        await meetings.Series.ExecuteDeleteAsync(ct);
        await meetings.SpecialDays.ExecuteDeleteAsync(ct);
    }
}
