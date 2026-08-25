using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// Which report scopes a viewer may ask for, and which one they land on by default.
/// </summary>
/// <remarks>
/// <para>
/// This is the visibility matrix as code, and it is the one place in S8 that reasons about roles. That looks like
/// a violation of "never filter by role in application code" and is not: the rule there is about <em>rows</em>,
/// and rows are still RLS's alone. This decides something different — which <em>question</em> a viewer may pose.
/// A member asking for the portfolio report must get a 403, not a silently empty portfolio.
/// </para>
/// <para>
/// The distinction matters both ways round. An empty report would tell a member the portfolio held nothing,
/// which is a lie; and letting them ask would be harmless to the data but useless to them, because RLS would
/// return their own rows under a title that says otherwise.
/// </para>
/// <para>
/// Pure and static, so the whole matrix row is unit-testable without a database, a request or a container.
/// </para>
/// </remarks>
public static class ReportScope
{
    /// <summary>
    /// The scopes this viewer may request, widest first.
    /// </summary>
    /// <remarks>
    /// Order is the point: the first entry is the default the report opens on, which is what §6 means by "the
    /// widest scope the viewer's role grants". Everything after it is what the narrower offers.
    /// </remarks>
    public static IReadOnlyList<string> Available(IUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.IsAuthenticated)
        {
            return [];
        }

        var scopes = new List<string>();

        if (user.Has(ContextualRole.Pmo))
        {
            scopes.Add(ReportScopes.Portfolio);
        }

        // The node scope is everybody's, and it is not a widening: which branch comes back is the node id, and
        // which node ids resolve is RLS. A member asking for their own node gets their own node; a head asking
        // for one above them gets a 404 from the same predicate that hides the rows.
        scopes.Add(ReportScopes.Node);

        if (user.HasAnyRole(ContextualRole.ProjectLead, ContextualRole.ProductOwner)
            || user.Has(ContextualRole.NodeHead)
            || user.Has(ContextualRole.Pmo))
        {
            scopes.Add(ReportScopes.Item);
        }

        scopes.Add(ReportScopes.Me);

        return scopes;
    }

    /// <summary>The scope a viewer lands on when they ask for none. Always the widest they hold.</summary>
    public static string Default(IUserContext user) => Available(user) is [var widest, ..]
        ? widest
        : ReportScopes.Me;

    /// <summary>
    /// Normalizes a requested scope, or refuses it.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="UnauthorizedAccessException"/> — a 403 — rather than quietly falling back to the default.
    /// A narrower that silently answered a different question than the one asked would be far harder to notice
    /// than an error, and the client draws its options from <see cref="Available"/> anyway, so this only fires on
    /// a hand-written URL.
    /// </remarks>
    public static string Resolve(IUserContext user, string? requested)
    {
        var available = Available(user);

        if (available.Count == 0)
        {
            throw new UnauthorizedAccessException("An unauthenticated session has no report scope.");
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return available[0];
        }

        var scope = requested.Trim().ToLowerInvariant();

        if (!ReportScopes.All.Contains(scope, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"'{requested}' is not a report scope. Use one of: {string.Join(", ", ReportScopes.All)}.");
        }

        if (!available.Contains(scope, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException($"Your role does not grant the '{scope}' report scope.");
        }

        return scope;
    }

    /// <summary>True where the scope names a specific thing the caller must identify.</summary>
    /// <remarks>
    /// Only the item scope. The node scope <em>accepts</em> an id and falls back to wherever the caller hangs off
    /// the tree, which is the answer they would have had to look up to ask the question.
    /// </remarks>
    public static bool RequiresScopeId(string scope) =>
        string.Equals(scope, ReportScopes.Item, StringComparison.Ordinal);

    /// <summary>
    /// True where the scope has a target at all, required or not.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RequiresScopeId"/> because the node scope has both shapes: a head naming a branch
    /// beneath them, and anybody naming none and meaning their own. Collapsing the two would either drop the id a
    /// head just picked or refuse the request everybody else makes.
    /// </remarks>
    public static bool AcceptsScopeId(string scope) =>
        RequiresScopeId(scope) || string.Equals(scope, ReportScopes.Node, StringComparison.Ordinal);
}
