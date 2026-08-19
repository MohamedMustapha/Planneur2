using System.Text;
using System.Text.Json;

// =================================================================================================================
// A deliberately dumb OpenAI-compatible server for the dev box and the integration tests.
//
// It exists so nobody needs a GPU to run `aspire run`, and so a test that asserts "the summary streamed in" is
// asserting about our streaming code rather than about a model's mood. It echoes a deterministic summary derived
// from the prompt, which makes assertions stable. It is never deployed.
// =================================================================================================================

var builder = WebApplication.CreateSlimBuilder(args);

var app = builder.Build();

app.MapGet("/v1/models", () => Results.Ok(new
{
    @object = "list",
    data = new[]
    {
        new { id = "local-model", @object = "model", owned_by = "cracra-stub" },
    },
}));

app.MapPost("/v1/chat/completions", async (HttpContext context) =>
{
    var request = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);

    var streaming = request.TryGetProperty("stream", out var stream) && stream.ValueKind is JsonValueKind.True;
    var model = request.TryGetProperty("model", out var m) ? m.GetString() ?? "local-model" : "local-model";
    var reply = BuildReply(request);

    if (!streaming)
    {
        return Results.Ok(new
        {
            id = "chatcmpl-stub",
            @object = "chat.completion",
            model,
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content = reply }, finish_reason = "stop" },
            },
            usage = new { prompt_tokens = 0, completion_tokens = reply.Length / 4, total_tokens = reply.Length / 4 },
        });
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    // Word by word, with a small delay, so the client's streaming path is genuinely exercised rather than
    // receiving one indistinguishable-from-non-streaming chunk.
    foreach (var word in reply.Split(' '))
    {
        var chunk = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-stub",
            @object = "chat.completion.chunk",
            model,
            choices = new[] { new { index = 0, delta = new { content = word + ' ' } } },
        });

        await context.Response.WriteAsync($"data: {chunk}\n\n", Encoding.UTF8, context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await Task.Delay(15, context.RequestAborted);
    }

    await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);

    return Results.Empty;
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

await app.RunAsync();

// The reply restates the requested language and the last user message, so a test can assert both the language
// plumbing and the prompt plumbing without ever guessing at generated prose.
static string BuildReply(JsonElement request)
{
    var language = "fr";
    var lastUserMessage = string.Empty;

    if (request.TryGetProperty("messages", out var messages) && messages.ValueKind is JsonValueKind.Array)
    {
        foreach (var message in messages.EnumerateArray())
        {
            var role = message.TryGetProperty("role", out var r) ? r.GetString() : null;
            var content = message.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;

            if (role is "system" && content.Contains("ISO 639-1", StringComparison.Ordinal))
            {
                var start = content.LastIndexOf('\'', content.Length - 1);
                var openQuote = content.LastIndexOf('\'', start - 1);

                if (start > 0 && openQuote >= 0)
                {
                    language = content[(openQuote + 1)..start];
                }
            }
            else if (role is "user")
            {
                lastUserMessage = content;
            }
        }
    }

    var preview = lastUserMessage.Length > 120 ? lastUserMessage[..120] : lastUserMessage;

    return $"[stub:{language}] Résumé généré localement. Prompt reçu : {preview}".Trim();
}
