using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Domain;
using Cracra.Modules.Access.Services;

namespace Cracra.Tests.Unit.Access;

/// <summary>
/// The merge that decides everyone's visibility: LDAP assignments ∪ active grants, minus active denies.
/// </summary>
/// <remarks>
/// Pure, so every rule is exercised here rather than through a container. These are the cases where being wrong is
/// invisible — nobody notices an extra role until they see something they should not have.
/// </remarks>
public sealed class EffectiveRolesTests
{
    private static readonly Guid Person = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDepartment = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ldap_assignments_come_through()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [Assignment(ContextualRole.Member, ScopeType.Unit, Unit)],
            [],
            Now);

        resolved.RoleNames.ShouldBe([ContextualRole.Member]);
        resolved.IsAuthoritative.ShouldBeTrue();
    }

    [Fact]
    public void A_grant_adds_a_role_LDAP_did_not_give()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [Assignment(ContextualRole.Member, ScopeType.Unit, Unit)],
            [Override(ContextualRole.UnitHead, ScopeType.Unit, Unit, isGrant: true)],
            Now);

        // The whole point of the fallback: LDAP is incomplete, and somebody has to be able to fix that without a
        // directory change ticket.
        resolved.RoleNames.ShouldBe([ContextualRole.Member, ContextualRole.UnitHead], ignoreOrder: true);
    }

    [Fact]
    public void A_deny_beats_an_LDAP_assignment()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [
                Assignment(ContextualRole.Member, ScopeType.Unit, Unit),
                Assignment(ContextualRole.DepartmentHead, ScopeType.Department, Department),
            ],
            [Override(ContextualRole.DepartmentHead, ScopeType.Department, Department, isGrant: false)],
            Now);

        // Revoking is the urgent direction — someone left, or was in the wrong group. It must not require finding
        // and deleting the LDAP-side grant first.
        resolved.RoleNames.ShouldBe([ContextualRole.Member]);
    }

    [Fact]
    public void A_deny_beats_a_grant_of_the_same_role_and_scope()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [],
            [
                Override(ContextualRole.Pmo, ScopeType.Global, null, isGrant: true),
                Override(ContextualRole.Pmo, ScopeType.Global, null, isGrant: false),
            ],
            Now);

        // Order-independent by construction. If a deny merely competed with a grant, the answer would depend on
        // which row came back first.
        resolved.RoleNames.ShouldBeEmpty();
    }

    [Fact]
    public void A_deny_is_scoped_and_does_not_reach_another_scope()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [
                Assignment(ContextualRole.DepartmentHead, ScopeType.Department, Department),
                Assignment(ContextualRole.DepartmentHead, ScopeType.Department, OtherDepartment),
            ],
            [Override(ContextualRole.DepartmentHead, ScopeType.Department, Department, isGrant: false)],
            Now);

        // Heading two departments and being removed from one is a real situation; the role survives for the other.
        resolved.Roles.Count.ShouldBe(1);
        resolved.Roles[0].ScopeId.ShouldBe(OtherDepartment);
    }

    [Fact]
    public void An_expired_grant_stops_applying()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [],
            [Override(ContextualRole.UnitHead, ScopeType.Unit, Unit, isGrant: true, expiresAt: Now.AddHours(-1))],
            Now);

        // Evaluated at resolution time, so a lapsed override stops applying without any job having to run.
        resolved.RoleNames.ShouldBeEmpty();
    }

    [Fact]
    public void An_expired_deny_stops_applying_too()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [Assignment(ContextualRole.UnitHead, ScopeType.Unit, Unit)],
            [Override(ContextualRole.UnitHead, ScopeType.Unit, Unit, isGrant: false, expiresAt: Now.AddHours(-1))],
            Now);

        // Symmetry matters: a temporary suspension that never lifts is its own kind of bug.
        resolved.RoleNames.ShouldBe([ContextualRole.UnitHead]);
    }

    [Fact]
    public void A_revoked_override_stops_applying()
    {
        var revoked = Override(ContextualRole.Pmo, ScopeType.Global, null, isGrant: true);
        revoked.RevokedAt = Now.AddMinutes(-5);

        var resolved = EffectiveRoles.Resolve(Person, [], [revoked], Now);

        resolved.RoleNames.ShouldBeEmpty();
    }

    [Fact]
    public void A_grant_that_duplicates_LDAP_does_not_double_up()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [Assignment(ContextualRole.Member, ScopeType.Unit, Unit)],
            [Override(ContextualRole.Member, ScopeType.Unit, Unit, isGrant: true)],
            Now);

        resolved.Roles.Count.ShouldBe(1);

        // LDAP wins the attribution. The role would exist without the override, and an admin reading the RBAC view
        // needs to see where it actually comes from.
        resolved.Roles[0].Source.ShouldBe(RoleSource.Ldap);
    }

    [Fact]
    public void No_rows_at_all_is_not_authoritative()
    {
        var resolved = EffectiveRoles.Resolve(Person, [], [], Now);

        // Before the first sync there is genuinely nothing on file, and the caller falls back to the token.
        resolved.IsAuthoritative.ShouldBeFalse();
    }

    [Fact]
    public void Denied_down_to_nothing_is_still_authoritative()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [Assignment(ContextualRole.Member, ScopeType.Unit, Unit)],
            [Override(ContextualRole.Member, ScopeType.Unit, Unit, isGrant: false)],
            Now);

        resolved.RoleNames.ShouldBeEmpty();

        // The distinction that stops a deny being undone by the token. Empty-and-authoritative means "no roles";
        // empty-and-not means "we cannot say" — treating them alike would restore the role we just removed.
        resolved.IsAuthoritative.ShouldBeTrue();
    }

    [Fact]
    public void Role_names_are_distinct_and_ordered()
    {
        var resolved = EffectiveRoles.Resolve(
            Person,
            [
                Assignment(ContextualRole.Member, ScopeType.Unit, Unit),
                Assignment(ContextualRole.Member, ScopeType.Department, Department),
                Assignment(ContextualRole.DepartmentHead, ScopeType.Department, Department),
            ],
            [],
            Now);

        // The GUC carries role names only; holding "member" over two scopes is still one name in app.roles.
        resolved.RoleNames.ShouldBe([ContextualRole.DepartmentHead, ContextualRole.Member]);
    }

    private static ContextualRoleAssignment Assignment(string role, ScopeType scopeType, Guid? scopeId) => new()
    {
        Id = Guid.CreateVersion7(),
        PersonId = Person,
        Role = role,
        ScopeType = scopeType,
        ScopeId = scopeId,
        Source = RoleSource.Ldap,
        CreatedAt = Now,
    };

    private static RbacOverride Override(
        string role,
        ScopeType scopeType,
        Guid? scopeId,
        bool isGrant,
        DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        PersonId = Person,
        Role = role,
        ScopeType = scopeType,
        ScopeId = scopeId,
        IsGrant = isGrant,
        Reason = "test",
        ExpiresAt = expiresAt,
        CreatedBy = Person,
        CreatedAt = Now.AddDays(-1),
    };
}
