using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// <c>visibility-matrix.md §6</c>, as assertions.
/// </summary>
/// <remarks>
/// The rule is small and the consequences of getting it wrong are not: too narrow and a department head cannot
/// run the report the whole slice exists for; too wide and a member gets a screen titled "department" filled with
/// their own unit's rows, which is worse than a refusal because it looks like an answer.
/// </remarks>
public sealed class ReportScopeTests
{
    [Fact]
    public void A_member_gets_their_own_work_and_their_project_teams()
    {
        ReportScope.Available(Person(ContextualRole.Member))
            .ShouldBe([ReportScopes.Team, ReportScopes.My]);
    }

    [Fact]
    public void A_member_lands_on_their_project_teams_by_default()
    {
        // The widest they hold, per §6: "My work + my project team(s)". Team is the wider of the two.
        ReportScope.Default(Person(ContextualRole.Member)).ShouldBe(ReportScopes.Team);
    }

    /// <summary>
    /// One role, and therefore one answer, at every depth (v2 §01.2).
    /// </summary>
    /// <remarks>
    /// The pre-v2 rule offered "department" to a dept-head and "unit" to a unit-head. With the level-named roles
    /// collapsed there is nothing left to tell those two apart from the role alone, so a head is offered both and
    /// RLS decides what each one returns — a head who picks a scope above their own branch gets nothing, rather
    /// than somebody else's rows. Narrowing the offer instead would need code that knows what a level is, which is
    /// the one thing §01 forbids.
    /// </remarks>
    [Fact]
    public void A_head_gets_every_scope_a_head_can_run()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.NodeHead));

        scopes[0].ShouldBe(ReportScopes.Department);
        scopes.ShouldContain(ReportScopes.Unit);
        scopes.ShouldContain(ReportScopes.Project);
        scopes.ShouldNotContain(ReportScopes.Portfolio);
    }

    [Fact]
    public void A_project_lead_gets_the_project_scope_without_the_unit_one()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.ProjectLead));

        scopes.ShouldContain(ReportScopes.Project);

        // Leading a project says nothing about heading a unit. Conflating the two is how an org chart quietly
        // becomes an access-control system.
        scopes.ShouldNotContain(ReportScopes.Unit);
    }

    [Fact]
    public void The_PMO_gets_everything_and_lands_on_the_portfolio()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.Pmo));

        scopes.ShouldBe(ReportScopes.All.Reverse().ToList(), ignoreOrder: true);
        scopes[0].ShouldBe(ReportScopes.Portfolio);
    }

    [Fact]
    public void An_anonymous_session_has_no_scope_at_all()
    {
        ReportScope.Available(UserContext.Anonymous).ShouldBeEmpty();

        Should.Throw<UnauthorizedAccessException>(() => ReportScope.Resolve(UserContext.Anonymous, null));
    }

    [Fact]
    public void Asking_for_a_scope_beyond_your_role_is_refused_rather_than_narrowed()
    {
        // 403, not a silent fallback to "my". A narrower that answered a different question than the one asked
        // would be far harder to notice than an error — and the client draws its options from Available anyway,
        // so this only fires on a hand-written URL.
        Should.Throw<UnauthorizedAccessException>(() =>
            ReportScope.Resolve(Person(ContextualRole.Member), ReportScopes.Department));
    }

    [Fact]
    public void An_unknown_scope_is_a_bad_request_rather_than_a_refusal()
    {
        // Different failure, different meaning: "there is no such scope" is the caller's mistake, not a boundary
        // they pushed against, and reporting it as a 403 would send somebody looking for permissions to fix.
        Should.Throw<DomainRuleViolationException>(() =>
            ReportScope.Resolve(Person(ContextualRole.Pmo), "everything"));
    }

    [Fact]
    public void Only_the_project_scope_takes_a_target()
    {
        ReportScope.RequiresScopeId(ReportScopes.Project).ShouldBeTrue();

        foreach (var scope in ReportScopes.All.Where(candidate => candidate != ReportScopes.Project))
        {
            ReportScope.RequiresScopeId(scope).ShouldBeFalse();
        }
    }

    private static UserContext Person(params string[] roles) => new()
    {
        IsAuthenticated = true,
        UserId = Guid.CreateVersion7(),
        UserName = "test",
        UnitId = Guid.CreateVersion7(),
        DepartmentIds = [Guid.CreateVersion7()],
        Roles = roles,
    };
}
