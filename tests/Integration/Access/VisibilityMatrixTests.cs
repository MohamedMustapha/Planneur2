using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Domain;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Cracra.Modules.Directory.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Tests.Integration.Access;

/// <summary>
/// The RLS matrix, asserted predicate by predicate against a real Postgres.
/// </summary>
/// <remarks>
/// <para>
/// This is what conventions.md §6 calls the executable form of <c>visibility-matrix.md</c>, and S2 calls its
/// keystone. Every later slice attaches its policies to these same functions, so proving them here means those
/// slices inherit a rule that has already been argued with rather than each restating it.
/// </para>
/// <para>
/// The org under test is the canonical fixture: two departments, two units each, plus one cross-department project
/// — the case the whole knowledge-flow rule exists for, and the one that is wrong in most access models.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class VisibilityMatrixTests(PostgresFixture postgres)
{
    private static readonly Guid SharedProject = Guid.Parse("99999999-0000-0000-0000-000000000001");
    private static readonly Guid PrivateProject = Guid.Parse("99999999-0000-0000-0000-000000000002");

    // --- can_read_activity ------------------------------------------------------------------------------------

    [Fact]
    public async Task Everyone_reads_their_own_activity()
    {
        await using var probe = await ProbeAsync();

        // Even with no roles at all. Your own row is the one thing that can never depend on a group membership.
        var stripped = SeedOrganisation.Camille with { Roles = [] };

        (await CanReadActivity(probe, stripped, owner: SeedOrganisation.Camille.UserId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_reads_their_unit_peers_activity()
    {
        await using var probe = await ProbeAsync();

        // Mehdi is in Camille's unit. The matrix grants this so kudos are possible at all — you cannot recognise
        // work you cannot see.
        (await CanReadActivity(
            probe,
            SeedOrganisation.Camille,
            owner: SeedOrganisation.Mehdi.UserId,
            unit: SeedOrganisation.Units.Infrastructure)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_does_not_read_another_units_activity_in_their_own_department()
    {
        await using var probe = await ProbeAsync();

        // Olivier is in the same department but a different unit. S1 widened the *directory* to the department so
        // colleagues are findable; activity stays unit-scoped, and this is the assertion that keeps those apart.
        (await CanReadActivity(
            probe,
            SeedOrganisation.Camille,
            owner: SeedOrganisation.Olivier.UserId,
            unit: SeedOrganisation.Units.Development)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_unit_head_reads_their_whole_unit()
    {
        await using var probe = await ProbeAsync();

        (await CanReadActivity(
            probe,
            SeedOrganisation.Thomas,
            owner: SeedOrganisation.Camille.UserId,
            unit: SeedOrganisation.Units.Infrastructure)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_unit_head_does_not_read_another_unit()
    {
        await using var probe = await ProbeAsync();

        (await CanReadActivity(
            probe,
            SeedOrganisation.Thomas,
            owner: SeedOrganisation.Olivier.UserId,
            unit: SeedOrganisation.Units.Development)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_department_head_reads_every_unit_in_their_department()
    {
        await using var probe = await ProbeAsync();

        (await CanReadActivity(
            probe,
            SeedOrganisation.Olivier,
            owner: SeedOrganisation.Camille.UserId,
            unit: SeedOrganisation.Units.Infrastructure)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_department_head_does_not_read_another_department()
    {
        await using var probe = await ProbeAsync();

        (await CanReadActivity(
            probe,
            SeedOrganisation.Olivier,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_department_head_reads_a_shared_project_across_the_boundary()
    {
        await using var probe = await ProbeAsync();

        // The one deliberate widening in the matrix. Sofia is in Finance, but her work on a project Olivier's
        // department also contributes to is visible to him — that is what "knowledge flow" means here.
        (await CanReadActivity(
            probe,
            SeedOrganisation.Olivier,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance,
            project: SharedProject)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_does_not_get_the_cross_department_widening()
    {
        await using var probe = await ProbeAsync();

        // The widening stops at heads. A member on the same shared project still does not get to read another
        // department's activity just because their department also contributes.
        (await CanReadActivity(
            probe,
            SeedOrganisation.Camille,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance,
            project: SharedProject)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_PMO_reads_everything()
    {
        await using var probe = await ProbeAsync();

        (await CanReadActivity(
            probe,
            SeedOrganisation.Nadia,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_unstamped_session_reads_nothing()
    {
        await using var probe = await ProbeAsync();

        // Fail closed. If the interceptor never ran, every predicate must be false rather than accidentally
        // matching on a NULL comparison.
        (await CanReadActivity(
            probe,
            UserContext.Anonymous,
            owner: SeedOrganisation.Camille.UserId,
            unit: SeedOrganisation.Units.Infrastructure)).ShouldBeFalse();
    }

    // --- Project scope ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_project_lead_reads_activity_on_their_project_regardless_of_department()
    {
        await using var probe = await ProbeAsync();

        var lead = SeedOrganisation.Camille with { Roles = [ContextualRole.Member, ContextualRole.ProjectLead] };

        (await CanReadActivity(
            probe,
            lead,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance,
            project: SharedProject)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_project_lead_reads_nothing_on_a_project_they_are_not_on()
    {
        await using var probe = await ProbeAsync();

        var lead = SeedOrganisation.Camille with { Roles = [ContextualRole.Member, ContextualRole.ProjectLead] };

        // The role is not a passport. It applies to projects they are actually a member of.
        (await CanReadActivity(
            probe,
            lead,
            owner: SeedOrganisation.Sofia.UserId,
            unit: SeedOrganisation.Units.Accounting,
            department: SeedOrganisation.Departments.Finance,
            project: PrivateProject)).ShouldBeFalse();
    }

    [Fact]
    public async Task On_project_reflects_the_membership_projection()
    {
        await using var probe = await ProbeAsync();

        (await probe.EvaluateAsync(SeedOrganisation.Camille, "access.on_project(@p0)", [SharedProject], TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await probe.EvaluateAsync(SeedOrganisation.Camille, "access.on_project(@p0)", [PrivateProject], TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    // --- can_read_kudo ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_parties_to_a_kudo_can_always_read_it()
    {
        await using var probe = await ProbeAsync();

        // Sofia is in another department entirely; she still sees a kudo she gave. One she gave vanishing because
        // the recipient moved would read as the system having lost it.
        (await probe.EvaluateAsync(
            SeedOrganisation.Sofia,
            "access.can_read_kudo(@p0, @p1, @p2, @p3)",
            [
                SeedOrganisation.Sofia.UserId,
                SeedOrganisation.Camille.UserId,
                SeedOrganisation.Units.Infrastructure,
                Ancestors(SeedOrganisation.Units.Infrastructure),
            ],
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_reads_kudos_within_their_unit()
    {
        await using var probe = await ProbeAsync();

        (await probe.EvaluateAsync(
            SeedOrganisation.Camille,
            "access.can_read_kudo(@p0, @p1, @p2, @p3)",
            [
                SeedOrganisation.Thomas.UserId,
                SeedOrganisation.Mehdi.UserId,
                SeedOrganisation.Units.Infrastructure,
                Ancestors(SeedOrganisation.Units.Infrastructure),
            ],
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_does_not_read_kudos_in_another_department()
    {
        await using var probe = await ProbeAsync();

        (await probe.EvaluateAsync(
            SeedOrganisation.Camille,
            "access.can_read_kudo(@p0, @p1, @p2, @p3)",
            [
                SeedOrganisation.Sofia.UserId,
                SeedOrganisation.Laurent.UserId,
                SeedOrganisation.Units.Accounting,
                Ancestors(SeedOrganisation.Units.Accounting),
            ],
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    // --- Write predicates -------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_owner_writes_their_own_rows()
    {
        await using var probe = await ProbeAsync();

        (await probe.EvaluateAsync(SeedOrganisation.Camille, "access.can_write_own(@p0)",
            [SeedOrganisation.Camille.UserId], TestContext.Current.CancellationToken)).ShouldBeTrue();

        // A unit head can *see* a member's week. Editing it is a different question, and the answer is no —
        // which is why the read and write predicates are separate functions.
        (await probe.EvaluateAsync(SeedOrganisation.Thomas, "access.can_write_own(@p0)",
            [SeedOrganisation.Camille.UserId], TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_unit_head_writes_within_their_unit_and_a_member_does_not()
    {
        await using var probe = await ProbeAsync();

        (await probe.EvaluateAsync(SeedOrganisation.Thomas, "access.can_write_in_node(@p0, @p1)",
            [SeedOrganisation.Units.Infrastructure, Ancestors(SeedOrganisation.Units.Infrastructure)],
            TestContext.Current.CancellationToken)).ShouldBeTrue();

        (await probe.EvaluateAsync(SeedOrganisation.Camille, "access.can_write_in_node(@p0, @p1)",
            [SeedOrganisation.Units.Infrastructure, Ancestors(SeedOrganisation.Units.Infrastructure)],
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    // --- Helpers ------------------------------------------------------------------------------------------------

    private static Task<bool> CanReadActivity(
        RlsMatrixProbe probe,
        IUserContext viewer,
        Guid owner,
        Guid? unit = null,
        Guid? department = null,
        Guid? project = null) =>
        probe.EvaluateAsync(
            viewer,
            "access.can_read_activity(@p0, @p1, @p2, @p3)",
            [
                owner,
                unit ?? SeedOrganisation.Units.Infrastructure,
                Ancestors(unit ?? SeedOrganisation.Units.Infrastructure),
                project,
            ],
            TestContext.Current.CancellationToken);

    /// <summary>The node path a row in this unit carries: root, its department, itself.</summary>
    private static Guid[] Ancestors(Guid unitId) =>
        [OrgTreeSql.UnclassifiedRootId, DepartmentOf(unitId), unitId];

    private static Guid DepartmentOf(Guid unitId) =>
        unitId == SeedOrganisation.Units.Accounting || unitId == SeedOrganisation.Units.Controlling
            ? SeedOrganisation.Departments.Finance
            : SeedOrganisation.Departments.InformationSystems;

    /// <summary>
    /// Brings up the schema, then seeds the project memberships the project-scoped predicates read.
    /// </summary>
    /// <remarks>
    /// The shared project has Camille (IS) and Sofia (Finance) on it — that is what makes it cross-department, and
    /// what makes the knowledge-flow assertions meaningful rather than vacuous.
    /// </remarks>
    private async Task<RlsMatrixProbe> ProbeAsync()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var ct = TestContext.Current.CancellationToken;

            await SeedTreeAsync(scope, ct);

            var context = scope.ServiceProvider.GetRequiredService<AccessDbContext>();

            await context.ProjectMemberships.ExecuteDeleteAsync(ct);

            context.ProjectMemberships.AddRange(
                Membership(SeedOrganisation.Camille.UserId, SharedProject, SeedOrganisation.Departments.InformationSystems),
                Membership(SeedOrganisation.Sofia.UserId, SharedProject, SeedOrganisation.Departments.Finance),
                Membership(SeedOrganisation.Sofia.UserId, PrivateProject, SeedOrganisation.Departments.Finance));

            await context.SaveChangesAsync(ct);
        }

        // The probe connects as the runtime role, the same one the application uses, so FORCE ROW LEVEL SECURITY
        // applies exactly as it does in production.
        return new RlsMatrixProbe(RuntimeConnectionString(factory));
    }

    /// <summary>
    /// The org as a tree, which is what the predicates now read.
    /// </summary>
    /// <remarks>
    /// Written directly rather than through directory sync: this fixture asserts the predicates, and going through
    /// Keycloak mapping to get four nodes would make a failure here ambiguous between the two.
    /// </remarks>
    private static async Task SeedTreeAsync(AsyncServiceScope scope, CancellationToken ct)
    {
        var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        if (await directory.OrgNodes.AnyAsync(node => node.Id == SeedOrganisation.Units.Infrastructure, ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        await directory.Database.ExecuteSqlRawAsync(
            """
            insert into directory.org_node (id, parent_id, level_no, code, name, active, created_at, modified_at)
            values ({0}, null, 1, 'A-RECLASSER', 'À reclasser', true, now(), now())
            on conflict (id) do nothing;
            """.Replace("{0}", $"'{OrgTreeSql.UnclassifiedRootId}'"),
            ct);

        foreach (var (id, parent, level, code) in Nodes())
        {
            directory.OrgNodes.Add(new OrgNode
            {
                Id = id,
                ParentId = parent,
                LevelNo = level,
                Code = code,
                Name = code,
                CreatedAt = now,
                ModifiedAt = now,
            });

            await directory.SaveChangesAsync(ct);
        }
    }

    private static IEnumerable<(Guid Id, Guid Parent, int Level, string Code)> Nodes()
    {
        yield return (SeedOrganisation.Departments.InformationSystems, OrgTreeSql.UnclassifiedRootId, 2, "IS");
        yield return (SeedOrganisation.Departments.Finance, OrgTreeSql.UnclassifiedRootId, 2, "FIN");
        yield return (SeedOrganisation.Units.Infrastructure, SeedOrganisation.Departments.InformationSystems, 3, "OPS");
        yield return (SeedOrganisation.Units.Development, SeedOrganisation.Departments.InformationSystems, 3, "DEV");
        yield return (SeedOrganisation.Units.Accounting, SeedOrganisation.Departments.Finance, 3, "ACC");
        yield return (SeedOrganisation.Units.Controlling, SeedOrganisation.Departments.Finance, 3, "CTL");
    }

    private static ProjectMembership Membership(Guid personId, Guid projectId, Guid departmentId) => new()
    {
        PersonId = personId,
        ProjectId = projectId,
        DepartmentId = departmentId,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static string RuntimeConnectionString(CracraApplicationFactory factory) =>
        factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<BuildingBlocks.Persistence.CracraDatabaseOptions>>()
            .Value
            .RuntimeConnectionString;
}
