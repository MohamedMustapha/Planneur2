using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Directory.Sync;

public sealed class DirectorySyncOptions
{
    public const string SectionName = "Cracra:Directory:Sync";

    /// <summary>Keycloak base URL, e.g. <c>http://localhost:8080</c>. Not the realm URL.</summary>
    [Required]
    [Url]
    public string KeycloakBaseUrl { get; set; } = string.Empty;

    [Required]
    public string Realm { get; set; } = "cracra";

    /// <summary>
    /// A service-account client with the <c>view-users</c> realm-management role. A service account rather than
    /// admin credentials: sync only ever reads the directory, and giving it an admin password would make a
    /// read-only job capable of rewriting the realm.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = "cracra-sync";

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>How often the scheduled reconciliation runs. Set to zero to disable it and sync on demand only.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Run once shortly after startup, so a fresh dev box has a populated directory without being asked.</summary>
    public bool SyncOnStartup { get; set; } = true;

    [Range(10, 1000)]
    public int PageSize { get; set; } = 200;
}

/// <summary>A user as Keycloak reports it. Only the fields the directory maps.</summary>
public sealed record KeycloakUser(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("firstName")] string? FirstName,
    [property: JsonPropertyName("lastName")] string? LastName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("attributes")] Dictionary<string, List<string>>? Attributes)
{
    /// <summary>
    /// Keycloak returns every custom attribute as a list, even single-valued ones. Reading the first entry is the
    /// normal case; the alternative is a null check at every call site.
    /// </summary>
    public string? Attribute(string name) =>
        Attributes is not null && Attributes.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;

    public IReadOnlyList<string> AttributeValues(string name) =>
        Attributes is not null && Attributes.TryGetValue(name, out var values) ? values : [];
}

/// <summary>
/// A Keycloak group. The realm models the org as department groups with unit subgroups, each carrying the ids the
/// directory keys on — so groups, not guesswork, are where the org structure comes from.
/// </summary>
public sealed record KeycloakGroup(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("attributes")] Dictionary<string, List<string>>? Attributes,
    [property: JsonPropertyName("subGroups")] List<KeycloakGroup>? SubGroups)
{
    public string? Attribute(string name) =>
        Attributes is not null && Attributes.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;
}

/// <summary>Reads people and groups out of Keycloak. Read-only by construction — there are no write methods.</summary>
public interface IKeycloakDirectoryClient
{
    Task<IReadOnlyList<KeycloakUser>> GetUsersAsync(CancellationToken ct);

    /// <summary>The org tree: department groups, each with its unit subgroups.</summary>
    Task<IReadOnlyList<KeycloakGroup>> GetGroupsAsync(CancellationToken ct);
}

internal sealed class KeycloakDirectoryClient(HttpClient http, IOptions<DirectorySyncOptions> options)
    : IKeycloakDirectoryClient
{
    private readonly DirectorySyncOptions _options = options.Value;

    public async Task<IReadOnlyList<KeycloakUser>> GetUsersAsync(CancellationToken ct)
    {
        await AuthenticateAsync(ct);

        var users = new List<KeycloakUser>();
        var first = 0;

        // Keycloak caps a single page regardless of what you ask for, so paging is not optional even for an org
        // that comfortably fits in one request today.
        while (true)
        {
            var page = await http.GetFromJsonAsync<List<KeycloakUser>>(
                $"admin/realms/{_options.Realm}/users?first={first}&max={_options.PageSize}&briefRepresentation=false",
                ct);

            if (page is null || page.Count == 0)
            {
                break;
            }

            users.AddRange(page);

            if (page.Count < _options.PageSize)
            {
                break;
            }

            first += page.Count;
        }

        return users;
    }

    public async Task<IReadOnlyList<KeycloakGroup>> GetGroupsAsync(CancellationToken ct)
    {
        await AuthenticateAsync(ct);

        // briefRepresentation=false is what makes Keycloak include the attributes; without them a group tells us
        // nothing we can key on.
        var departments = await http.GetFromJsonAsync<List<KeycloakGroup>>(
            $"admin/realms/{_options.Realm}/groups?briefRepresentation=false&max=1000",
            ct) ?? [];

        // Children are fetched per group rather than inline. Keycloak 23+ stopped returning subGroups from the
        // list endpoint — populateHierarchy=true is accepted and silently returns an empty array — so relying on
        // it produces an org with departments, no units, and every person skipped for referencing a unit that
        // "does not exist". One request per department, and departments are few.
        var withUnits = new List<KeycloakGroup>(departments.Count);

        foreach (var department in departments)
        {
            var children = await http.GetFromJsonAsync<List<KeycloakGroup>>(
                $"admin/realms/{_options.Realm}/groups/{department.Id}/children?briefRepresentation=false&max=1000",
                ct) ?? [];

            withUnits.Add(department with { SubGroups = children });
        }

        return withUnits;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        // A token per sync run. Runs are hourly and a client-credentials grant is cheap, so caching it would add
        // expiry handling for no measurable gain.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"realms/{_options.Realm}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            }),
        };

        using var response = await http.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token?.AccessToken ?? throw new InvalidOperationException(
                "Keycloak returned no access token for the directory sync service account."));
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
