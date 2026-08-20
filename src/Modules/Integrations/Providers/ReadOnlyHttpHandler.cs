using System.Net.Http.Headers;
using Cracra.Modules.Integrations.Services;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Integrations.Providers;

/// <summary>
/// The socket-level half of the read-only guarantee.
/// </summary>
/// <remarks>
/// <para>
/// Sits under every provider's <c>HttpClient</c> and refuses anything that could change the other system. GET
/// passes. POST passes only to a path the provider declared as a query endpoint — Azure DevOps' WIQL is a POST
/// because the query does not fit in a URL, which is the one legitimate reason a read is a POST anywhere in this
/// module. PUT, PATCH, DELETE and everything else throw before a byte leaves the process.
/// </para>
/// <para>
/// It throws rather than returning 405. A refused write is a bug in this codebase, not a condition to handle: if
/// a future adapter tries one, the sync fails loudly with the offending verb and path in the message, which is
/// the outcome that gets it fixed. A soft failure would show up as a connection that silently mirrors nothing.
/// </para>
/// <para>
/// It also enforces the egress allow-list. The connection's base URL is validated when it is written, but the
/// value on the row and the value in a redirect are different things — a provider that followed a 302 to
/// somewhere unlisted would have left the allow-list behind entirely, so the check lives where every request
/// passes rather than where the row is saved.
/// </para>
/// </remarks>
internal sealed class ReadOnlyHttpHandler(IOptions<IntegrationsOptions> options) : DelegatingHandler
{
    /// <summary>
    /// Path suffixes a POST may target, because the provider's own API models a query as one.
    /// </summary>
    /// <remarks>
    /// Suffixes rather than full paths: a DevOps collection lives under an arbitrary prefix, and a rule that
    /// depended on the prefix would be a rule each deployment could get wrong. Every entry here must be a
    /// documented query endpoint of its provider, and there are two.
    /// </remarks>
    private static readonly string[] QueryPostPaths =
    [
        // Azure DevOps: Work Item Query Language. Returns ids; the fields come back through a GET.
        "/_apis/wit/wiql",

        // ServiceNow: the aggregate endpoint used for counts. Also a read expressed as a POST.
        "/api/now/stats",
    ];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri
                  ?? throw new InvalidOperationException("An integration request was made without a URI.");

        EnsureAllowedHost(uri);
        EnsureReadOnly(request.Method, uri);

        return base.SendAsync(request, ct);
    }

    private void EnsureReadOnly(HttpMethod method, Uri uri)
    {
        if (method == HttpMethod.Get || method == HttpMethod.Head)
        {
            return;
        }

        if (method == HttpMethod.Post && QueryPostPaths.Any(path =>
                uri.AbsolutePath.EndsWith(path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Integrations are read-only: {method} {uri.AbsolutePath} was refused. Only GET, and POST to a "
            + "declared query endpoint, may leave this module.");
    }

    private void EnsureAllowedHost(Uri uri)
    {
        var allowed = options.Value.AllowedHosts;

        // An empty allow-list permits everything, and is the documented default for a dev box pointed at a stub
        // on localhost. A deployment that means it sets the list; one that has not, has not yet decided.
        if (allowed.Count == 0)
        {
            return;
        }

        if (!allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The host '{uri.Host}' is not in Cracra:Integrations:AllowedHosts.");
        }
    }

    /// <summary>Turns a resolved credential into the header its provider expects.</summary>
    /// <remarks>
    /// Here rather than in each provider because getting it wrong fails identically for both — a 203 of HTML
    /// login page that deserializes to zero items — and one place to look is worth the small indirection.
    /// </remarks>
    public static AuthenticationHeaderValue? Authorization(IntegrationCredential credential) =>
        credential.Kind switch
        {
            // Azure DevOps takes the PAT as the password of a Basic pair with an empty username. It looks wrong
            // and is documented; the alternative header shapes are all silently rejected.
            IntegrationCredential.PersonalAccessToken => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + credential.Value))),
            IntegrationCredential.Basic => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credential.Value))),
            IntegrationCredential.Bearer => new AuthenticationHeaderValue("Bearer", credential.Value),
            _ => null,
        };
}
