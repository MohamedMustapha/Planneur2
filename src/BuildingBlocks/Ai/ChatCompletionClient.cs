using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Cracra.BuildingBlocks.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Cracra:Ai";

    /// <summary>
    /// Base URL of the on-prem, OpenAI-API-compatible server. This must never point at a public endpoint —
    /// the prompts carry staff names, project costs and activity detail.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    [Required]
    public string Model { get; set; } = "local-model";

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.2;

    [Range(64, 32_000)]
    public int MaxTokens { get; set; } = 2_000;

    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed record ChatMessage(string Role, string Content)
{
    public static ChatMessage System(string content) => new("system", content);

    public static ChatMessage User(string content) => new("user", content);
}

/// <summary>
/// The on-prem LLM. S8 uses it for weekly and monthly narrative summaries; nothing else should, and nothing at all
/// should let it decide access — it summarizes rows RLS already allowed the caller to see.
/// </summary>
public interface IChatCompletionClient
{
    /// <summary>
    /// <paramref name="language"/> is the viewer's active UI language; it is stated in the prompt rather than
    /// inferred, because a summary that silently switches language reads as a bug to the person reading it.
    /// </summary>
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, string language, CancellationToken ct = default);

    /// <summary>Token-by-token, for the streaming summary panel in the report screens.</summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string language,
        CancellationToken ct = default);
}

internal sealed class OpenAiCompatibleChatClient(HttpClient http, IOptions<AiOptions> options) : IChatCompletionClient
{
    private const string CompletionsPath = "v1/chat/completions";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AiOptions _options = options.Value;

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string language,
        CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(CompletionsPath, BuildRequest(messages, language, stream: false), Json, ct);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(Json, ct);

        return payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string language,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CompletionsPath)
        {
            Content = JsonContent.Create(BuildRequest(messages, language, stream: true), options: Json),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();

            if (data is "[DONE]")
            {
                yield break;
            }

            var chunk = JsonSerializer.Deserialize<ChatCompletionResponse>(data, Json);
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;

            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    private ChatCompletionRequest BuildRequest(IReadOnlyList<ChatMessage> messages, string language, bool stream)
        => new(
            _options.Model,
            [
                new ChatCompletionMessage("system", $"Answer strictly in the language with ISO 639-1 code '{language}'."),
                .. messages.Select(message => new ChatCompletionMessage(message.Role, message.Content)),
            ],
            _options.Temperature,
            _options.MaxTokens,
            stream);

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatCompletionMessage> Messages,
        double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        bool Stream);

    private sealed record ChatCompletionMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatCompletionChoice>? Choices);

    private sealed record ChatCompletionChoice(ChatCompletionMessage? Message, ChatCompletionMessage? Delta);
}

internal sealed class LlmHealthCheck(HttpClient http) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync("v1/models", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded($"The model endpoint answered {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The on-prem model endpoint is not reachable.", ex);
        }
    }
}

public static class AiExtensions
{
    public const string HttpClientName = "cracra-llm";

    public static IServiceCollection AddCracraAi(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

                client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + '/');
                client.Timeout = settings.Timeout;

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // Generation is slow by nature; the default per-attempt timeout would cancel healthy requests.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
            });

        services.AddTransient<IChatCompletionClient>(serviceProvider =>
            new OpenAiCompatibleChatClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                serviceProvider.GetRequiredService<IOptions<AiOptions>>()));

        // The health check talks to the same endpoint but must not inherit the resilience handler: a circuit
        // breaker that hides the outage from the probe is worse than no probe at all.
        services.AddTransient(serviceProvider => new LlmHealthCheck(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.AddHealthChecks()
            .AddCheck<LlmHealthCheck>("llm", tags: ["ready", "ai"]);

        return services;
    }
}
