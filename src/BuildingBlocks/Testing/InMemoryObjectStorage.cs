using System.Collections.Concurrent;
using Cracra.BuildingBlocks.Storage;

namespace Cracra.BuildingBlocks.Testing;

/// <summary>
/// Object storage in a dictionary, for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// RustFS is not in the loop for the same reason Keycloak is not: what these tests need to vary is our own
/// behaviour — that a report renders, that the object is keyed the way we intend, that a link comes back — and
/// standing up an S3 server to assert those would be testing AWS's SDK. The real adapter is exercised by the
/// dev box and by the Playwright suite, where the whole stack is up.
/// </para>
/// <para>
/// The presigned URL it returns is well-formed and expires, but is not fetchable. A test asserting on the bytes
/// should read them through <see cref="GetAsync"/>, which is what actually stored them.
/// </para>
/// </remarks>
public sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    /// <summary>Every key written so far, for tests that assert on the layout rather than on the content.</summary>
    public IReadOnlyCollection<string> Keys => [.. objects.Keys];

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();

        await content.CopyToAsync(buffer, ct);

        objects[key] = buffer.ToArray();
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct = default) =>
        objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new FileNotFoundException($"No object at {key}.");

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(objects.ContainsKey(key));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        objects.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task<Uri> PresignGetAsync(string key, TimeSpan? lifetime = null, CancellationToken ct = default) =>
        Task.FromResult(new Uri(
            $"https://storage.invalid/{key}?expires={DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)).ToUnixTimeSeconds()}"));
}
