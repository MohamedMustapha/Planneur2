using System.Security.Claims;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cracra.Tests.Unit.Web;

/// <summary>
/// The claims-to-scope translation. Everything RLS decides comes from what this produces, so its failure modes
/// matter more than its happy path — and all of them must fail closed.
/// </summary>
public sealed class UserContextTests
{
    [Fact]
    public void Builds_the_full_scope_from_Keycloak_claims()
    {
        var principal = Principal(
            (CracraClaims.Subject, "c0000000-0000-0000-0000-000000000004"),
            (CracraClaims.PreferredUsername, "thomas.berthier"),
            (CracraClaims.UnitId, "aaaaaaaa-0000-0000-0000-000000000001"),
            (CracraClaims.DepartmentIds, "11111111-1111-1111-1111-111111111111"),
            (CracraClaims.ContextualRoles, "member"),
            (CracraClaims.ContextualRoles, "unit-head"),
            (CracraClaims.Locale, "fr"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        context.IsAuthenticated.ShouldBeTrue();
        context.UserName.ShouldBe("thomas.berthier");
        context.UnitId.ShouldBe(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        context.DepartmentIds.ShouldBe([Guid.Parse("11111111-1111-1111-1111-111111111111")]);
        context.Roles.ShouldBe(["member", "unit-head"], ignoreOrder: true);
        context.Language.ShouldBe("fr");
    }

    [Fact]
    public void Accepts_a_multi_valued_claim_delivered_as_one_comma_separated_value()
    {
        // Whether Keycloak emits repeated claims or one joined value depends on the mapper's "multivalued" flag.
        // Both shapes have to work, because a realm tweak must not silently empty someone's department scope.
        var principal = Principal(
            (CracraClaims.Subject, "c0000000-0000-0000-0000-000000000009"),
            (CracraClaims.DepartmentIds, "11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        context.DepartmentIds.Count.ShouldBe(2);
    }

    [Fact]
    public void Everyone_authenticated_is_at_least_a_member()
    {
        var principal = Principal((CracraClaims.Subject, "c0000000-0000-0000-0000-000000000001"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        // "member" is what grants sight of one's own unit in the matrix; without it an ordinary employee could not
        // see the teammates they are supposed to be able to give kudos to.
        context.Has(ContextualRole.Member).ShouldBeTrue();
    }

    [Fact]
    public void A_token_cannot_claim_the_system_role()
    {
        var principal = Principal(
            (CracraClaims.Subject, "c0000000-0000-0000-0000-000000000001"),
            (CracraClaims.ContextualRoles, "system"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        // 'system' is read-all for background jobs. If a forged or misconfigured token could assert it, the whole
        // visibility matrix would be one claim away from being bypassed.
        context.Has(ContextualRole.System).ShouldBeFalse();
    }

    [Fact]
    public void Unknown_roles_are_discarded()
    {
        var principal = Principal(
            (CracraClaims.Subject, "c0000000-0000-0000-0000-000000000001"),
            (CracraClaims.ContextualRoles, "super-admin"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        context.Roles.ShouldBe([ContextualRole.Member]);
    }

    [Fact]
    public void An_unparsable_subject_yields_an_empty_scope_rather_than_a_partial_one()
    {
        var principal = Principal(
            (CracraClaims.Subject, "not-a-guid"),
            (CracraClaims.UnitId, "aaaaaaaa-0000-0000-0000-000000000001"),
            (CracraClaims.ContextualRoles, "pmo"));

        var context = UserContextMiddleware.Build(principal, NullLogger.Instance);

        // Authenticated but unidentifiable: the unit and the PMO role are dropped too, so every RLS predicate
        // evaluates false. Fewer rows than expected is a recoverable bug; more is a breach.
        context.UserId.ShouldBe(Guid.Empty);
        context.UnitId.ShouldBeNull();
        context.Roles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("ES", "es")]
    [InlineData("en_GB", "en")]
    [InlineData("de-DE", "fr")]
    [InlineData(null, "fr")]
    public void Normalizes_locales_to_a_supported_language(string? locale, string expected)
    {
        SupportedLanguages.Normalize(locale).ShouldBe(expected);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticationType: "test");

        return new ClaimsPrincipal(identity);
    }
}
