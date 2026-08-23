namespace Cracra.BuildingBlocks.Web.Users;

/// <summary>
/// The ambient identity for the current request or background operation. Everything the RLS session needs
/// (<c>architecture.md §4</c>) comes from here, so this is the single place a request's scope is decided.
/// </summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string UserName { get; }

    Guid? UnitId { get; }

    IReadOnlyList<Guid> DepartmentIds { get; }

    Guid? NodeId { get; }

    IReadOnlyList<Guid> NodePath { get; }

    IReadOnlyList<Guid> HeadedNodes { get; }

    /// <summary>Contextual roles — see <see cref="ContextualRole"/>.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Active UI language (fr / en / es). Drives keyed server text and the AI summary prompt.</summary>
    string Language { get; }

    bool Has(string role);

    /// <summary>
    /// True when any of these roles is held.
    /// </summary>
    /// <remarks>
    /// A default implementation rather than a member each type repeats: what "any of" means is not something an
    /// implementation should get to disagree about, and two of them already exist.
    /// </remarks>
    bool HasAnyRole(params string[] roles) => roles.Any(Has);
}

/// <summary>
/// Scoped holder the <c>UserContextMiddleware</c> writes to and everything else reads from. Tests swap the value
/// directly to "become" any role without minting a token.
/// </summary>
public interface IUserContextAccessor
{
    IUserContext Current { get; set; }
}

public sealed record UserContext : IUserContext
{
    public static readonly UserContext Anonymous = new();

    /// <summary>The context background jobs run under: <c>app.roles = 'system'</c>, no unit, no department.</summary>
    public static readonly UserContext SystemJob = new()
    {
        IsAuthenticated = true,
        UserId = Guid.Empty,
        UserName = "system",
        Roles = [ContextualRole.System],
    };

    public bool IsAuthenticated { get; init; }

    public Guid UserId { get; init; }

    public string UserName { get; init; } = "anonymous";

    public Guid? UnitId { get; init; }

    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];

    public Guid? NodeId { get; init; }

    public IReadOnlyList<Guid> NodePath { get; init; } = [];

    public IReadOnlyList<Guid> HeadedNodes { get; init; } = [];

    public IReadOnlyList<string> Roles { get; init; } = [];

    public string Language { get; init; } = SupportedLanguages.Default;

    public bool Has(string role) => Roles.Contains(role, StringComparer.Ordinal);
}

/// <summary>fr / en / es — <c>conventions.md §5</c>.</summary>
public static class SupportedLanguages
{
    public const string French = "fr";
    public const string English = "en";
    public const string Spanish = "es";

    public const string Default = French;

    public static readonly IReadOnlyList<string> All = [French, English, Spanish];

    /// <summary>Normalizes anything culture-shaped ("fr-FR", "ES") to a supported two-letter code.</summary>
    public static string Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Default;
        }

        var twoLetter = candidate.Trim().Split('-', '_')[0].ToLowerInvariant();

        return All.Contains(twoLetter, StringComparer.Ordinal) ? twoLetter : Default;
    }
}

internal sealed class UserContextAccessor : IUserContextAccessor
{
    public IUserContext Current { get; set; } = UserContext.Anonymous;
}

/// <summary>
/// Forwards every read to <see cref="IUserContextAccessor.Current"/> so consumers always observe the context as of
/// the moment they ask, not as of the moment they were constructed.
/// </summary>
internal sealed class UserContextProxy(IUserContextAccessor accessor) : IUserContext
{
    public bool IsAuthenticated => accessor.Current.IsAuthenticated;

    public Guid UserId => accessor.Current.UserId;

    public string UserName => accessor.Current.UserName;

    public Guid? UnitId => accessor.Current.UnitId;

    public IReadOnlyList<Guid> DepartmentIds => accessor.Current.DepartmentIds;

    public Guid? NodeId => accessor.Current.NodeId;

    public IReadOnlyList<Guid> NodePath => accessor.Current.NodePath;

    public IReadOnlyList<Guid> HeadedNodes => accessor.Current.HeadedNodes;

    public IReadOnlyList<string> Roles => accessor.Current.Roles;

    public string Language => accessor.Current.Language;

    public bool Has(string role) => accessor.Current.Has(role);
}
