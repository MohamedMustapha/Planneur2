using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Tests.Unit.Reporting;

/// <summary>
/// The visibility matrix, as assertions.
/// </summary>
/// <remarks>
/// The rule is small and the consequences of getting it wrong are not: too narrow and a head cannot run the
/// report the whole slice exists for; too wide and a member gets a screen titled "portfolio" filled with their
/// own rows, which is worse than a refusal because it looks like an answer.
/// </remarks>
public sealed class ReportScopeTests
{
    [Fact]
    public void A_member_gets_their_own_branch_and_their_own_week()
    {
        ReportScope.Available(Person(ContextualRole.Member))
            .ShouldBe([ReportScopes.Node, ReportScopes.Me]);
    }

    [Fact]
    public void A_member_lands_on_their_branch_by_default()
    {
        // The widest they hold. Their branch is wider than their week and is the one that answers "what is my
        // team doing", which is the question a member arrives at this page with.
        ReportScope.Default(Person(ContextualRole.Member)).ShouldBe(ReportScopes.Node);
    }

    /// <summary>
    /// One role, one answer, at every depth (v2 §01.2).
    /// </summary>
    /// <remarks>
    /// The pre-v2 rule offered "department" to a dept-head and "unit" to a unit-head, which needed code that knew
    /// what a level was. There is one node scope now and the node id carries the depth — so a head at any rung is
    /// offered the same thing, and which branch comes back is RLS's answer rather than the enum's.
    /// </remarks>
    [Fact]
    public void A_head_is_offered_the_same_scopes_as_every_other_head()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.NodeHead));

        scopes[0].ShouldBe(ReportScopes.Node);
        scopes.ShouldContain(ReportScopes.Item);
        scopes.ShouldNotContain(ReportScopes.Portfolio);
    }

    [Fact]
    public void No_scope_names_a_level()
    {
        // The point of the slice. A scope called "unit" or "department" is a fourth level away from needing a
        // fourth scope, a fourth translation key and a fourth branch in the composer.
        foreach (var scope in ReportScopes.All)
        {
            scope.ShouldNotBeOneOf("unit", "department", "service", "bureau", "team");
        }
    }

    [Fact]
    public void A_project_lead_gets_the_item_scope_without_heading_anything()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.ProjectLead));

        scopes.ShouldContain(ReportScopes.Item);

        // Leading an item says nothing about running a branch — but the branch scope is everybody's, because it
        // resolves to their own. What a lead does not get is the portfolio.
        scopes.ShouldContain(ReportScopes.Node);
        scopes.ShouldNotContain(ReportScopes.Portfolio);
    }

    [Fact]
    public void The_PMO_gets_everything_and_lands_on_the_portfolio()
    {
        var scopes = ReportScope.Available(Person(ContextualRole.Member, ContextualRole.Pmo));

        scopes.ShouldBe(ReportScopes.All.ToList(), ignoreOrder: true);
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
        // 403, not a silent fallback to "me". A narrower that answered a different question than the one asked
        // would be far harder to notice than an error — and the client draws its options from Available anyway,
        // so this only fires on a hand-written URL.
        Should.Throw<UnauthorizedAccessException>(() =>
            ReportScope.Resolve(Person(ContextualRole.Member), ReportScopes.Portfolio));
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
    public void A_level_named_scope_no_longer_resolves_at_all()
    {
        // The old vocabulary is gone rather than aliased. A bookmarked ?scope=department should say so plainly.
        foreach (var retired in new[] { "unit", "department", "team", "project", "my" })
        {
            Should.Throw<DomainRuleViolationException>(() =>
                ReportScope.Resolve(Person(ContextualRole.Pmo), retired));
        }
    }

    [Fact]
    public void The_branch_scope_accepts_a_target_without_demanding_one()
    {
        // Both shapes are real: a head naming a branch beneath them, and anybody naming none and meaning their
        // own. Collapsing the two drops the id a head just picked, which is how a branch report silently
        // answers about the wrong branch.
        ReportScope.AcceptsScopeId(ReportScopes.Node).ShouldBeTrue();
        ReportScope.RequiresScopeId(ReportScopes.Node).ShouldBeFalse();

        ReportScope.AcceptsScopeId(ReportScopes.Me).ShouldBeFalse();
        ReportScope.AcceptsScopeId(ReportScopes.Portfolio).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_item_scope_demands_a_target()
    {
        ReportScope.RequiresScopeId(ReportScopes.Item).ShouldBeTrue();

        // The branch scope accepts one and falls back to wherever the caller hangs off the tree, which is the
        // answer they would otherwise have had to look up before they could ask the question.
        foreach (var scope in ReportScopes.All.Where(candidate => candidate != ReportScopes.Item))
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
