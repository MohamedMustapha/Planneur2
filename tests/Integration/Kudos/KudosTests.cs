using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Kudos.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Kudos;

/// <summary>
/// Kudos end to end, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The eligibility rules, the cap and the ladder are unit-tested exhaustively without a database. What only a real
/// stack can prove is the visibility matrix — that a unit peer genuinely sees a colleague's recognition and a
/// neighbouring department genuinely does not, decided by a policy in Postgres rather than by a filter in a
/// handler — and that the table refuses to let anybody rewrite what they said.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class KudosTests(PostgresFixture postgres)
{
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    private const string PointsMode = """{"mode":"points-badges-leaderboard"}""";

    // --- Giving ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_member_can_recognise_a_unit_peer()
    {
        await using var factory = await SeededAsync();

        var result = await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        result.Category.ShouldBe("cleanup");

        // Counter mode by default: the department has said nothing, so nobody is shown a score.
        result.ShowsPoints.ShouldBeFalse();
        result.Points.ShouldBe(0);
        result.RemainingThisMonth.ShouldBe(9);
    }

    [Fact]
    public async Task Nobody_can_recognise_themselves()
    {
        await using var factory = await SeededAsync();

        var response = await PostAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Camille.UserId, "cleanup");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Somebody_in_another_department_cannot_be_recognised()
    {
        await using var factory = await SeededAsync();

        // Camille is in IS, Sofia in Finance, and no project connects them. There is nothing she could have seen
        // Sofia do.
        var response = await PostAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Sofia.UserId, "cleanup");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_project_teammate_in_another_department_can_be_recognised()
    {
        await using var factory = await SeededAsync();
        await CreateCrossDepartmentProjectAsync(factory);

        // The other half of eligibility. The unit rule would refuse this; sharing a project is exactly the case
        // it exists for.
        var response = await PostAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Sofia.UserId, "mentoring");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_department_head_can_recognise_anybody_in_their_department()
    {
        await using var factory = await SeededAsync();

        // Olivier heads IS from the Development unit; Camille is in Infrastructure. No shared unit, no project.
        var response = await PostAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Camille.UserId, "initiative");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_head_of_another_department_still_cannot()
    {
        await using var factory = await SeededAsync();

        var response = await PostAsync(factory, SeedOrganisation.Laurent, SeedOrganisation.Camille.UserId, "initiative");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_category_the_department_does_not_offer_is_refused()
    {
        await using var factory = await SeededAsync();

        var response = await PostAsync(
            factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi.UserId, "employee-of-the-month");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_department_subtype_can_be_given_alongside_the_canonical_categories()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(
            factory,
            """{"mode":"points","categories":[{"code":"on-call-rescue","labelKey":"is.oncall","points":4}]}""");

        var result = await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "on-call-rescue");

        result.Points.ShouldBe(4);

        // Merged, not replaced: adding one must not cost the department the vocabulary S8 reports in.
        var canonical = await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        canonical.Points.ShouldBe(1);
    }

    // --- The cap ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_monthly_cap_refuses_the_one_after_it()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(factory, """{"monthlyCapPerGiver":2}""");

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");
        var second = await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        second.RemainingThisMonth.ShouldBe(0);

        var third = await PostAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi.UserId, "cleanup");

        third.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_cap_is_per_giver_not_per_pair()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(factory, """{"monthlyCapPerGiver":1}""");

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        // Mehdi has spent nothing. Camille's exhausted allowance is hers alone.
        var response = await PostAsync(factory, SeedOrganisation.Mehdi, SeedOrganisation.Camille.UserId, "cleanup");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_rules_endpoint_reports_the_remaining_allowance()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(factory, """{"monthlyCapPerGiver":3}""");
        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        factory.AsUser(SeedOrganisation.Camille);

        var rules = await factory.CreateClient().GetFromJsonAsync<KudoRulesView>(
            $"/api/kudos/rules?personId={SeedOrganisation.Mehdi.UserId}",
            TestContext.Current.CancellationToken);

        rules.ShouldNotBeNull();
        rules.MonthlyCapPerGiver.ShouldBe(3);
        rules.GivenThisMonth.ShouldBe(1);
        rules.RemainingThisMonth.ShouldBe(2);
        rules.Categories.Select(category => category.Code).ShouldContain("initiative");
    }

    // --- Visibility ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_unit_peer_sees_recognition_given_inside_the_unit()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        // Thomas heads Infrastructure and is a member of it. The wall is what makes recognition public inside a
        // team; a private kudo would be an email.
        var wall = await WallAsync(factory, SeedOrganisation.Thomas, "unit");

        wall.ShouldHaveSingleItem().ToPersonId.ShouldBe(SeedOrganisation.Mehdi.UserId);
    }

    [Fact]
    public async Task A_foreign_department_sees_none_of_it()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        (await WallAsync(factory, SeedOrganisation.Sofia, "unit")).ShouldBeEmpty();
        (await WallAsync(factory, SeedOrganisation.Laurent, "department")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Both_parties_always_see_their_own_kudo()
    {
        await using var factory = await SeededAsync();
        await CreateCrossDepartmentProjectAsync(factory);

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Sofia, "mentoring");

        // The kudo carries Sofia's unit, which Camille has no other claim on. A kudo she gave disappearing from
        // her own list would read as the system having lost it.
        (await WallAsync(factory, SeedOrganisation.Camille, "me", direction: "given")).ShouldHaveSingleItem();
        (await WallAsync(factory, SeedOrganisation.Sofia, "me")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_department_head_sees_the_whole_department()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        (await WallAsync(factory, SeedOrganisation.Olivier, "department")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_pmo_reads_a_unit_in_a_department_they_do_not_belong_to()
    {
        await using var factory = await SeededAsync();

        // Laurent heads Finance and Sofia is in it: a head recognising inside their own scope.
        await GiveAsync(factory, SeedOrganisation.Laurent, SeedOrganisation.Sofia, "initiative");

        var pmo = await SummaryAsync(
            factory, SeedOrganisation.Nadia, "unit", SeedOrganisation.Units.Accounting);

        pmo.Total.ShouldBe(1);

        // The same question, asked by somebody with no claim on Finance. Not an error — the honest answer to
        // "how much recognition is there in that unit" for a reader who may not see any of it.
        var member = await SummaryAsync(
            factory, SeedOrganisation.Camille, "unit", SeedOrganisation.Units.Accounting);

        member.Total.ShouldBe(0);
    }

    [Fact]
    public async Task A_kudo_cannot_be_edited_by_anybody_including_its_author()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Camille;

        var context = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Kudos.Infrastructure.KudosDbContext>();

        var ct = TestContext.Current.CancellationToken;

        // There is no endpoint for this on purpose; the point of the assertion is that there could not usefully
        // be one. With an insert policy and no update or delete policy, Postgres simply matches no rows.
        var updated = await context.Kudos.ExecuteUpdateAsync(
            setters => setters.SetProperty(kudo => kudo.Message, "Something else entirely"), ct);

        var deleted = await context.Kudos.ExecuteDeleteAsync(ct);

        updated.ShouldBe(0);
        deleted.ShouldBe(0);
    }

    // --- Mode ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_leaderboard_is_refused_where_the_department_only_counts()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .GetAsync("/api/kudos/leaderboard?scope=unit", TestContext.Current.CancellationToken);

        // 403, not an empty list: "nobody has been recognised here" and "we do not rank people here" are
        // different statements and only one of them is true.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Even_the_head_who_set_counter_mode_is_refused_the_leaderboard()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient()
            .GetAsync("/api/kudos/leaderboard?scope=department", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_counter_works_whatever_the_mode()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "initiative");

        var summary = await SummaryAsync(factory, SeedOrganisation.Camille, "unit");

        summary.Total.ShouldBe(2);
        summary.ShowsPoints.ShouldBeFalse();
        summary.MyGiven.ShouldBe(1);
        summary.MyReceived.ShouldBe(0);

        var mehdi = summary.PerPerson.ShouldHaveSingleItem();

        mehdi.PersonId.ShouldBe(SeedOrganisation.Mehdi.UserId);
        mehdi.Count.ShouldBe(2);

        // A counting department never sees a score, even as a zero it could infer something from.
        mehdi.Points.ShouldBe(0);
        mehdi.Badges.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_points_on_reveals_what_was_already_recorded()
    {
        await using var factory = await SeededAsync();

        // Given while the department was a bare counter.
        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "above-and-beyond");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "above-and-beyond");

        (await SummaryAsync(factory, SeedOrganisation.Camille, "unit")).PerPerson
            .ShouldHaveSingleItem().Points.ShouldBe(0);

        await ConfigureRulesAsync(factory, PointsMode);

        var summary = await SummaryAsync(factory, SeedOrganisation.Camille, "unit");

        // This is the acceptance criterion, and the reason the row carries a price it does not show: a leaderboard
        // that starts at zero on the day of a settings change is a leaderboard nobody believes.
        summary.ShowsPoints.ShouldBeTrue();
        summary.PerPerson.ShouldHaveSingleItem().Points.ShouldBe(10);
    }

    [Fact]
    public async Task The_leaderboard_ranks_and_the_badges_appear()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(factory, PointsMode);

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "above-and-beyond");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "above-and-beyond");
        await GiveAsync(factory, SeedOrganisation.Mehdi, SeedOrganisation.Camille, "cleanup");

        factory.AsUser(SeedOrganisation.Camille);

        var board = await factory.CreateClient().GetFromJsonAsync<LeaderboardView>(
            "/api/kudos/leaderboard?scope=unit",
            TestContext.Current.CancellationToken);

        board.ShouldNotBeNull();
        board.Rows.Count.ShouldBe(2);

        var top = board.Rows[0];

        top.Rank.ShouldBe(1);
        top.PersonId.ShouldBe(SeedOrganisation.Mehdi.UserId);
        top.Points.ShouldBe(10);

        // Ten points crosses helping-hand, and the first kudo crossed first-kudo before it.
        top.Badges.Select(badge => badge.Code).ShouldBe(["first-kudo", "helping-hand"]);

        board.Rows[1].Rank.ShouldBe(2);
        board.Rows[1].Points.ShouldBe(1);
    }

    [Fact]
    public async Task Giving_reports_the_badge_it_just_crossed()
    {
        await using var factory = await SeededAsync();

        await ConfigureRulesAsync(factory, PointsMode);

        var first = await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "above-and-beyond");

        first.EarnedBadgeCodes.ShouldBe(["first-kudo"]);

        var second = await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "above-and-beyond");

        second.EarnedBadgeCodes.ShouldBe(["helping-hand"]);
    }

    // --- The annual claim -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_annual_view_groups_a_year_by_category_and_keeps_the_messages()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup",
            message: "Cleared the build warnings nobody else would touch.");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "cleanup",
            message: "Tidied the runbooks.");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "mentoring",
            message: "Walked the new joiner through the on-call rota.");

        factory.AsUser(SeedOrganisation.Mehdi);

        var annual = await factory.CreateClient().GetFromJsonAsync<AnnualKudosView>(
            "/api/kudos/me/annual",
            TestContext.Current.CancellationToken);

        annual.ShouldNotBeNull();
        annual.Total.ShouldBe(3);

        var biggest = annual.ByCategory[0];

        biggest.Category.ShouldBe("cleanup");
        biggest.Count.ShouldBe(2);

        // The messages are the point: a count without the sentence proves nothing to a reviewer.
        biggest.Kudos.Select(kudo => kudo.Message).ShouldContain("Tidied the runbooks.");
    }

    [Fact]
    public async Task The_annual_view_is_the_callers_own_and_nobody_elses()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");

        factory.AsUser(SeedOrganisation.Camille);

        var annual = await factory.CreateClient().GetFromJsonAsync<AnnualKudosView>(
            "/api/kudos/me/annual",
            TestContext.Current.CancellationToken);

        // Camille gave one and received none. The endpoint takes no person parameter at all, so the only way to
        // read somebody else's claim view is to be them.
        annual.ShouldNotBeNull();
        annual.PersonId.ShouldBe(SeedOrganisation.Camille.UserId);
        annual.Total.ShouldBe(0);
    }

    // --- Eligibility list -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_picker_offers_unit_peers_and_never_the_caller()
    {
        await using var factory = await SeededAsync();

        var peers = await EligibleAsync(factory, SeedOrganisation.Camille);

        peers.Select(peer => peer.PersonId).ShouldContain(SeedOrganisation.Mehdi.UserId);
        peers.Select(peer => peer.PersonId).ShouldNotContain(SeedOrganisation.Camille.UserId);
        peers.Select(peer => peer.PersonId).ShouldNotContain(SeedOrganisation.Sofia.UserId);
    }

    [Fact]
    public async Task The_picker_gains_a_project_teammate_from_another_department()
    {
        await using var factory = await SeededAsync();
        await CreateCrossDepartmentProjectAsync(factory);

        var peers = await EligibleAsync(factory, SeedOrganisation.Camille);

        var sofia = peers.SingleOrDefault(peer => peer.PersonId == SeedOrganisation.Sofia.UserId);

        // The list is the eligibility rule, so what it offers and what the write accepts are the same set.
        sofia.ShouldNotBeNull();
        sofia.Relation.ShouldBe("project");
    }

    // --- The S8 seam ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_unit_report_counts_kudos_now_that_there_are_some()
    {
        await using var factory = await SeededAsync();

        await GiveAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi, "cleanup");
        await GiveAsync(factory, SeedOrganisation.Thomas, SeedOrganisation.Mehdi, "initiative");

        factory.AsUser(SeedOrganisation.Thomas);

        var report = await factory.CreateClient().GetFromJsonAsync<ReportEnvelope>(
            "/api/reports?scope=unit&period=month",
            TestContext.Current.CancellationToken);

        report.ShouldNotBeNull();

        var qol = report.Sections.Single(section => section.Key == "qol");

        // The figure S8 has been rendering as zero since it shipped. One registration changed; the report's
        // contract, its PDF and its Angular view did not.
        qol.Metrics.Single(metric => metric.Key == "kudos").Value.ShouldBe(2);
    }

    // --- Fixture ---------------------------------------------------------------------------------------------

    private sealed record GiveRequest(Guid ToPersonId, string Category, string? Message);

    private sealed record GiveResponse(
        Guid Id,
        string Category,
        int Points,
        bool ShowsPoints,
        int RemainingThisMonth,
        IReadOnlyList<string> EarnedBadgeCodes);

    private sealed record CreatedResponse(Guid Id);

    private sealed record ReportEnvelope(IReadOnlyList<ReportSectionEnvelope> Sections);

    private sealed record ReportSectionEnvelope(string Key, IReadOnlyList<ReportMetricEnvelope> Metrics);

    private sealed record ReportMetricEnvelope(string Key, decimal Value);

    private static async Task<HttpResponseMessage> PostAsync(
        CracraApplicationFactory factory,
        IUserContext giver,
        Guid receiverId,
        string category,
        string? message = "Stayed late to unblock the release.")
    {
        factory.AsUser(giver);

        return await factory.CreateClient().PostAsJsonAsync(
            "/api/kudos",
            new GiveRequest(receiverId, category, message),
            TestContext.Current.CancellationToken);
    }

    private static async Task<GiveResponse> GiveAsync(
        CracraApplicationFactory factory,
        IUserContext giver,
        IUserContext receiver,
        string category,
        string? message = "Stayed late to unblock the release.")
    {
        var response = await PostAsync(factory, giver, receiver.UserId, category, message);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<GiveResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<KudoView>> WallAsync(
        CracraApplicationFactory factory,
        IUserContext reader,
        string scope,
        string? direction = null)
    {
        factory.AsUser(reader);

        var query = direction is null ? $"?scope={scope}" : $"?scope={scope}&direction={direction}";

        return (await factory.CreateClient().GetFromJsonAsync<List<KudoView>>(
            $"/api/kudos{query}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<KudosSummary> SummaryAsync(
        CracraApplicationFactory factory,
        IUserContext reader,
        string scope,
        Guid? scopeId = null)
    {
        factory.AsUser(reader);

        var query = scopeId is { } id ? $"?scope={scope}&scopeId={id}" : $"?scope={scope}";

        return (await factory.CreateClient().GetFromJsonAsync<KudosSummary>(
            $"/api/kudos/summary{query}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<EligiblePeer>> EligibleAsync(
        CracraApplicationFactory factory,
        IUserContext reader)
    {
        factory.AsUser(reader);

        return (await factory.CreateClient().GetFromJsonAsync<List<EligiblePeer>>(
            "/api/kudos/eligible",
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Rewrites the IS department's kudo rules as its head, which is who is allowed to.</summary>
    private static async Task ConfigureRulesAsync(CracraApplicationFactory factory, string kudoRulesJson)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            new
            {
                activityTaxonomyJson = "{}",
                roleLabelsJson = "{}",
                kudoRulesJson,
                defaultBoardLayout = "week",
                iterationPresetsJson = """["1w","2w","1m"]""",
                weeklyTargetHours = 35m,
                enforceWeeklyTarget = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateCrossDepartmentProjectAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-M365",
                name = "Migration M365",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = new[] { SeedOrganisation.Departments.Finance },
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        foreach (var (person, department) in new[]
                 {
                     (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems),
                     (SeedOrganisation.Sofia, SeedOrganisation.Departments.Finance),
                 })
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new
                {
                    personId = person.UserId,
                    departmentId = department,
                    functionalRoleId = DevRole,
                },
                ct);

            added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return projectId;
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

    /// <summary>
    /// Clears everything each scenario counts. The Postgres container is shared for speed, and a kudo left behind
    /// would be picked up by assertions several of which are about emptiness.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var kudos = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Kudos.Infrastructure.KudosDbContext>();

        // The system context is the only one that may: the delete policy names it and nothing else, which is half
        // of what A_kudo_cannot_be_edited asserts — the other half being that not even this context can rewrite a
        // message, because there is no update policy for anybody.
        await kudos.Kudos.ExecuteDeleteAsync(ct);

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await projects.ProjectMembers.ExecuteDeleteAsync(ct);
        await projects.ProjectDepartments.ExecuteDeleteAsync(ct);
        await projects.Projects.ExecuteDeleteAsync(ct);

        var access = scope.ServiceProvider.GetRequiredService<Cracra.Modules.Access.Data.AccessDbContext>();

        await access.ProjectMemberships.ExecuteDeleteAsync(ct);

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);

        // Reset rather than delete: sync creates a config only when it creates the department, and departments
        // survive this reset.
        await directory.DepartmentConfigs.ExecuteUpdateAsync(
            setters => setters.SetProperty(config => config.KudoRulesJson, "{}"),
            ct);
    }
}
