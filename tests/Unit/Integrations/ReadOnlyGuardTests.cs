using System.Net;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Services;
using Microsoft.Extensions.Options;

namespace Cracra.Tests.Unit.Integrations;

/// <summary>
/// The socket-level half of S10's read-only guarantee.
/// </summary>
/// <remarks>
/// The architecture test proves nothing in the module <em>references</em> a mutating HTTP member. This proves the
/// other half: that if something ever did — through reflection, a library, a redirect — it would not reach the
/// wire. The two overlap on purpose, because they fail at different moments and catch different mistakes.
/// </remarks>
public sealed class ReadOnlyGuardTests
{
    [Fact]
    public async Task A_get_passes()
    {
        var response = await SendAsync(HttpMethod.Get, "https://devops.intranet/_apis/wit/workitems?ids=1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_post_to_a_declared_query_endpoint_passes()
    {
        // WIQL is a POST because a work-item query does not fit in a URL. That is the one exception, and it is
        // an allow-list rather than a rule about POST in general.
        var response = await SendAsync(HttpMethod.Post, "https://devops.intranet/CRACRA/_apis/wit/wiql?api-version=7.1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Anything_that_could_change_the_other_system_is_refused(string verb)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            SendAsync(new HttpMethod(verb), "https://devops.intranet/_apis/wit/workitems/4301"));

        // Thrown rather than answered with a status: a refused write is a bug in this codebase, not a condition
        // to handle, and the message has to name the verb and the path for whoever has to fix it.
        exception.Message.ShouldContain(verb);
        exception.Message.ShouldContain("read-only");
    }

    [Fact]
    public async Task A_post_anywhere_else_is_refused()
    {
        // The shape a write-back would actually take: DevOps' work-item update is a PATCH, but its comment API
        // is a POST, and "POST is how we query" would have quietly permitted it.
        await Should.ThrowAsync<InvalidOperationException>(
            SendAsync(HttpMethod.Post, "https://devops.intranet/CRACRA/_apis/wit/workItems/$Task"));
    }

    [Fact]
    public async Task A_host_outside_the_allow_list_is_refused()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            SendAsync(HttpMethod.Get, "https://elsewhere.example/_apis/wit/workitems", ["devops.intranet"]));

        exception.Message.ShouldContain("elsewhere.example");
    }

    [Fact]
    public async Task An_empty_allow_list_permits_any_host()
    {
        // The documented dev-box default: a stub on localhost, and no list. A deployment that means it sets one.
        var response = await SendAsync(HttpMethod.Get, "http://localhost:5300/_apis/wit/workitems");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(IntegrationCredential.PersonalAccessToken, "token", "Basic")]
    [InlineData(IntegrationCredential.Basic, "user:password", "Basic")]
    [InlineData(IntegrationCredential.Bearer, "jwt", "Bearer")]
    public void Each_credential_kind_becomes_the_header_its_provider_expects(
        string kind,
        string value,
        string expectedScheme)
    {
        var header = ReadOnlyHttpHandler.Authorization(new IntegrationCredential(kind, value));

        header.ShouldNotBeNull();
        header.Scheme.ShouldBe(expectedScheme);
    }

    [Fact]
    public void A_personal_access_token_goes_in_as_the_password_of_an_empty_pair()
    {
        // It looks wrong and is what Azure DevOps documents. Every other shape is silently rejected with a 203
        // and an HTML sign-in page, which deserializes to zero items and reads as "the sprint is empty".
        var header = ReadOnlyHttpHandler.Authorization(
            new IntegrationCredential(IntegrationCredential.PersonalAccessToken, "abc123"));

        var decoded = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(header!.Parameter!));

        decoded.ShouldBe(":abc123");
    }

    [Fact]
    public void An_unresolvable_credential_produces_no_header_at_all()
    {
        // Not an empty one. A request that carries "Authorization: Basic " is answered with a 401 that looks like
        // a wrong password; one that carries nothing is answered with a 401 that looks like what it is.
        ReadOnlyHttpHandler.Authorization(IntegrationCredential.None).ShouldBeNull();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string[]? allowedHosts = null)
    {
        var options = new IntegrationsOptions();

        foreach (var host in allowedHosts ?? [])
        {
            options.AllowedHosts.Add(host);
        }

        var handler = new ReadOnlyHttpHandler(Options.Create(options))
        {
            InnerHandler = new AlwaysOkHandler(),
        };

        using var client = new HttpClient(handler);

        return await client.SendAsync(new HttpRequestMessage(method, url), TestContext.Current.CancellationToken);
    }

    /// <summary>Stands in for the far side. Nothing here should ever be reached by a mutating request.</summary>
    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
