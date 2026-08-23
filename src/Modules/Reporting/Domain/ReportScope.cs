using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// Which report scopes a viewer may ask for, and which one they land on by default.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>visibility-matrix.md §6</c> as code, and it is the one place in S8 that reasons about roles. That
/// looks like a violation of "never filter by role in application code" and is not: the rule there is about
/// <em>rows</em>, and rows are still RLS's alone. This decides something different — which <em>question</em> a
/// viewer may pose. A member asking for the department report must get a 403, not a silently empty department.
/// </para>
/// <para>
/// The distinction matters both ways round. An empty report would tell a member their department did nothing,
/// which is a lie; and letting them ask would be harmless to the data but useless to them, because RLS would
/// return their own unit's rows under a title that says "department".
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

        if (user.Has(ContextualRole.NodeHead) || user.Has(ContextualRole.Pmo))
        {
            scopes.Add(ReportScopes.Department);
            scopes.Add(ReportScopes.Unit);
        }

        if (user.HasAnyRole(ContextualRole.ProjectLead, ContextualRole.ProductOwner)
            || user.Has(ContextualRole.NodeHead)
            || user.Has(ContextualRole.Pmo))
        {
            scopes.Add(ReportScopes.Project);
        }

        // Everyone gets these two. "My project team" is not the same as "my project": a member sees the teams of
        // the projects they are on, which is exactly what the matrix grants them and no more.
        scopes.Add(ReportScopes.Team);
        scopes.Add(ReportScopes.My);

        return scopes;
    }

    /// <summary>The scope a viewer lands on when they ask for none. Always the widest they hold.</summary>
    public static string Default(IUserContext user) => Available(user) is [var widest, ..]
        ? widest
        : ReportScopes.My;

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
    /// Only the project scope. The rest are derived from who the caller is — your unit, your department — and
    /// asking for somebody else's would be a question RLS answers with silence rather than with a refusal.
    /// </remarks>
    public static bool RequiresScopeId(string scope) =>
        string.Equals(scope, ReportScopes.Project, StringComparison.Ordinal);
}
