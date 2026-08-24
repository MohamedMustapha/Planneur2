using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Problems.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Meetings;

/// <summary>
/// The compte-rendu through HTTP, with real RLS (v2 §07).
/// </summary>
/// <remarks>
/// The claims worth proving against real rows are the ones the slice exists for: a draft belongs to whoever is
/// writing it, publishing is what distributes it to the level's scope and no further, and an action closes when
/// the work it points at resolves — which is the difference between a tracker and a dead document.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MinutesTests(PostgresFixture postgres)
{
    private static readonly DateOnly Monday = new(2026, 8, 17);

    [Fact]
    public async Task The_meeting_owner_opens_the_minutes_and_they_start_as_a_draft()
    {
        await using var factory = await SeededAsync();

        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        minutes.Published.ShouldBeFalse();
        minutes.Level.ShouldBe(MeetingLevels.Node);
        minutes.AuthorPersonId.ShouldBe(SeedOrganisation.Olivier.UserId);
    }

    [Fact]
    public async Task Opening_twice_reopens_the_same_draft_rather_than_forking_it()
    {
        await using var factory = await SeededAsync();

        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));

        var first = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);
        var second = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task A_draft_is_nobody_elses_business_until_it_is_published()
    {
        await using var factory = await SeededAsync();

        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        await AmendAsync(factory, SeedOrganisation.Olivier, minutes.Id, "Two incidents, both closed.");

        (await LatestAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();

        await PublishAsync(factory, SeedOrganisation.Olivier, minutes.Id);

        (await LatestAsync(factory, SeedOrganisation.Camille)).Select(digest => digest.Id).ShouldContain(minutes.Id);
    }

    [Fact]
    public async Task A_published_node_CR_reaches_the_node_and_stops_there()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        (await LatestAsync(factory, SeedOrganisation.Camille)).Select(digest => digest.Id).ShouldContain(minutes.Id);
        (await LatestAsync(factory, SeedOrganisation.Thomas)).Select(digest => digest.Id).ShouldContain(minutes.Id);

        // Sofia is in Finance. A CR that reached her would be a distribution list nobody asked for.
        (await LatestAsync(factory, SeedOrganisation.Sofia)).Select(digest => digest.Id).ShouldNotContain(minutes.Id);
    }

    [Fact]
    public async Task The_PMO_reads_every_CR()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        (await LatestAsync(factory, SeedOrganisation.Nadia)).Select(digest => digest.Id).ShouldContain(minutes.Id);
    }

    [Fact]
    public async Task A_cross_node_CR_reaches_every_node_it_names()
    {
        await using var factory = await SeededAsync();

        var series = await CreateSeriesAsync(
            factory,
            SeedOrganisation.Olivier,
            level: MeetingLevels.CrossNode,
            scopeIds: [SeedOrganisation.Units.Infrastructure, SeedOrganisation.Units.Development]);

        series.ScopeIds.Count.ShouldBe(2);

        var occurrence = await FirstOccurrenceAsync(factory, series);
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        minutes.Level.ShouldBe(MeetingLevels.CrossNode);

        await AmendAsync(factory, SeedOrganisation.Olivier, minutes.Id, "The shared platform gets one owner.");
        await PublishAsync(factory, SeedOrganisation.Olivier, minutes.Id);

        // Camille sits in Infrastructure, one of the two named nodes.
        (await LatestAsync(factory, SeedOrganisation.Camille)).Select(digest => digest.Id).ShouldContain(minutes.Id);
        (await LatestAsync(factory, SeedOrganisation.Sofia)).Select(digest => digest.Id).ShouldNotContain(minutes.Id);
    }

    [Fact]
    public async Task A_cross_node_series_has_to_say_which_nodes()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/series",
            Payload(level: MeetingLevels.CrossNode, scopeIds: []),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_reader_of_the_CR_is_not_a_writer_of_it()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/meetings/minutes/{minutes.Id}",
            new { summary = "Actually it went badly." },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_empty_CR_is_refused_rather_than_distributed()
    {
        await using var factory = await SeededAsync();

        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/minutes/{minutes.Id}/publish",
            new { },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Decisions_and_actions_are_enough_to_publish_without_a_summary()
    {
        await using var factory = await SeededAsync();

        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Olivier);

        (await factory.CreateClient().PostAsJsonAsync(
                $"/api/meetings/minutes/{minutes.Id}/decisions",
                new { text = "The migration goes ahead in September.", decidedBy = "le COPIL" },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var published = await PublishAsync(factory, SeedOrganisation.Olivier, minutes.Id);

        published.Decisions.Single().DecidedBy.ShouldBe("le COPIL");

        var digest = (await LatestAsync(factory, SeedOrganisation.Camille)).Single(row => row.Id == minutes.Id);

        digest.DecisionCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_action_lands_on_its_owners_tracker_even_when_somebody_else_wrote_it()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        await AssignAsync(factory, minutes.Id, SeedOrganisation.Camille.UserId, "Write the runbook");

        var mine = await ActionsAsync(factory, SeedOrganisation.Camille);

        mine.Select(action => action.Title).ShouldContain("Write the runbook");

        // And it is not on somebody else's list just because they can read the CR.
        (await ActionsAsync(factory, SeedOrganisation.Thomas)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_overdue_action_says_so_and_the_owner_can_settle_it()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        await AssignAsync(
            factory,
            minutes.Id,
            SeedOrganisation.Camille.UserId,
            "Write the runbook",
            due: new DateOnly(2020, 1, 1));

        var overdue = await ActionsAsync(factory, SeedOrganisation.Camille, status: "overdue");
        var action = overdue.Single();

        action.Overdue.ShouldBeTrue();

        factory.AsUser(SeedOrganisation.Camille);

        // Camille may not touch the minutes; she may close what she owes. That split is the whole reason the
        // action policy is not the minutes policy.
        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/meetings/minutes/{minutes.Id}/actions/{action.Id}",
            new { status = ActionStatuses.Done },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ActionsAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_linked_problem_resolving_closes_the_action()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);
        var problemId = Guid.CreateVersion7();

        await AssignAsync(
            factory,
            minutes.Id,
            SeedOrganisation.Camille.UserId,
            "Script the last three steps",
            linkType: ActionLinkTypes.Problem,
            linkId: problemId);

        (await ActionsAsync(factory, SeedOrganisation.Camille)).Count.ShouldBe(1);

        await ResolveAsync(factory, problemId);

        // v2 §07.2: closing the linked thing closes the action, so nobody maintains the same completion twice.
        (await ActionsAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();

        var all = await ActionsAsync(factory, SeedOrganisation.Camille, status: "all");

        all.Single().Status.ShouldBe(ActionStatuses.Done);
    }

    [Fact]
    public async Task An_unrelated_problem_resolving_leaves_the_action_alone()
    {
        await using var factory = await SeededAsync();

        var minutes = await PublishedNodeMinutesAsync(factory);

        await AssignAsync(
            factory,
            minutes.Id,
            SeedOrganisation.Camille.UserId,
            "Script the last three steps",
            linkType: ActionLinkTypes.Problem,
            linkId: Guid.CreateVersion7());

        await ResolveAsync(factory, Guid.CreateVersion7());

        (await ActionsAsync(factory, SeedOrganisation.Camille)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_occurrence_says_whether_its_CR_exists_and_whether_it_went_out()
    {
        await using var factory = await SeededAsync();

        var series = await NodeSeriesAsync(factory);
        var before = await FirstOccurrenceAsync(factory, series);

        before.MinutesId.ShouldBeNull();
        before.Level.ShouldBe(MeetingLevels.Node);

        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, before.Id);

        var during = await FirstOccurrenceAsync(factory, series);

        during.MinutesId.ShouldBe(minutes.Id);
        during.MinutesPublished.ShouldBeFalse();

        await AmendAsync(factory, SeedOrganisation.Olivier, minutes.Id, "Nothing blocking.");
        await PublishAsync(factory, SeedOrganisation.Olivier, minutes.Id);

        (await FirstOccurrenceAsync(factory, series)).MinutesPublished.ShouldBeTrue();
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    private static async Task<MinutesView> PublishedNodeMinutesAsync(CracraApplicationFactory factory)
    {
        var occurrence = await FirstOccurrenceAsync(factory, await NodeSeriesAsync(factory));
        var minutes = await OpenAsync(factory, SeedOrganisation.Olivier, occurrence.Id);

        await AmendAsync(factory, SeedOrganisation.Olivier, minutes.Id, "Two incidents, both closed.");

        return await PublishAsync(factory, SeedOrganisation.Olivier, minutes.Id);
    }

    private static Task<MeetingSeriesView> NodeSeriesAsync(CracraApplicationFactory factory) =>
        CreateSeriesAsync(factory, SeedOrganisation.Olivier);

    private static async Task<MeetingSeriesView> CreateSeriesAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string level = MeetingLevels.Node,
        IReadOnlyList<Guid>? scopeIds = null)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/meetings/series",
            Payload(level, scopeIds),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<MeetingSeriesView>(
            TestContext.Current.CancellationToken))!;
    }

    private static object Payload(string level, IReadOnlyList<Guid>? scopeIds) => new
    {
        kind = MeetingKinds.WeeklyNode,
        nameKey = "meetings.kind.weeklyNode",
        scopeType = MeetingScopeTypes.Department,
        scopeId = SeedOrganisation.Departments.InformationSystems,
        recurrenceRule = "FREQ=WEEKLY;BYDAY=MO",
        startsOn = Monday.ToString("yyyy-MM-dd"),
        startTime = "09:00:00",
        timeZoneId = "Europe/Paris",
        durationMinutes = 60,
        active = true,
        level,
        scopeIds = scopeIds ?? [],
    };

    private static async Task<MeetingOccurrenceView> FirstOccurrenceAsync(
        CracraApplicationFactory factory,
        MeetingSeriesView series)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var occurrences = await factory.CreateClient().GetFromJsonAsync<List<MeetingOccurrenceView>>(
            $"/api/meetings/occurrences?from={Monday:yyyy-MM-dd}&to={Monday.AddDays(7):yyyy-MM-dd}",
            TestContext.Current.CancellationToken);

        return occurrences!.First(occurrence => occurrence.SeriesId == series.Id);
    }

    private static async Task<MinutesView> OpenAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid occurrenceId)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/occurrences/{occurrenceId}/minutes",
            new { },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<MinutesView>(TestContext.Current.CancellationToken))!;
    }

    private static async Task AmendAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid minutesId,
        string summary)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/meetings/minutes/{minutesId}",
            new { summary },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<MinutesView> PublishAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid minutesId)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/minutes/{minutesId}/publish",
            new { },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<MinutesView>(TestContext.Current.CancellationToken))!;
    }

    private static async Task AssignAsync(
        CracraApplicationFactory factory,
        Guid minutesId,
        Guid owner,
        string title,
        DateOnly? due = null,
        string? linkType = null,
        Guid? linkId = null)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/meetings/minutes/{minutesId}/actions",
            new
            {
                title,
                ownerPersonId = owner,
                due = due?.ToString("yyyy-MM-dd"),
                linkType = linkType ?? ActionLinkTypes.None,
                linkId,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<IReadOnlyList<ActionItemView>> ActionsAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string status = "open")
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<ActionItemView>>(
            $"/api/meetings/actions?status={status}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<MinutesDigest>> LatestAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<MinutesDigest>>(
            "/api/meetings/minutes",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task ResolveAsync(CracraApplicationFactory factory, Guid problemId)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        // Published straight rather than drained from an outbox: what is under test is the listener's reaction,
        // not the drain, which has its own tests.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        await scope.ServiceProvider.GetRequiredService<IPublisher>()
            .Publish(new ProblemResolved(problemId, null), TestContext.Current.CancellationToken);
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
        var meetings = scope.ServiceProvider.GetRequiredService<Modules.Meetings.Data.MeetingsDbContext>();

        // Deleted rather than truncated: app_rw is granted the four verbs a request needs and not the one that
        // would let a bug empty a table wholesale, and the reset runs as the application, not as its owner.
        await meetings.Actions.ExecuteDeleteAsync(ct);
        await meetings.Decisions.ExecuteDeleteAsync(ct);
        await meetings.Minutes.ExecuteDeleteAsync(ct);
        await meetings.Attendance.ExecuteDeleteAsync(ct);
        await meetings.Occurrences.ExecuteDeleteAsync(ct);
        await meetings.Series.ExecuteDeleteAsync(ct);
    }
}
