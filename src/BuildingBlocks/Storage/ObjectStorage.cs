using System.ComponentModel.DataAnnotations;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Cracra.BuildingBlocks.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "Cracra:Storage";

    /// <summary>RustFS endpoint. S3-compatible, on-prem, never a public bucket.</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string Bucket { get; set; } = "cracra";

    /// <summary>
    /// RustFS, like MinIO, addresses buckets by path rather than by virtual host, so this stays true for anything
    /// that is not real AWS.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:30", "24:00:00")]
    public TimeSpan DefaultPresignLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Object storage for exported reports and attachments. Deliberately narrow: the platform stores and retrieves
/// blobs and hands out short-lived links, and nothing else needs to know it is S3 underneath.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// A time-limited download URL. Reports are served this way rather than proxied through the API so a large
    /// PDF never occupies a request thread.
    /// </summary>
    Task<Uri> PresignGetAsync(string key, TimeSpan? lifetime = null, CancellationToken ct = default);
}

internal sealed class S3ObjectStorage(IAmazonS3 client, IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    private readonly ObjectStorageOptions _options = options.Value;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        => await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                DisablePayloadSigning = true,
            },
            ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var response = await client.GetObjectAsync(_options.Bucket, key, ct);

        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await client.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await client.DeleteObjectAsync(_options.Bucket, key, ct);

    public Task<Uri> PresignGetAsync(string key, TimeSpan? lifetime = null, CancellationToken ct = default)
    {
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime ?? _options.DefaultPresignLifetime),
        });

        return Task.FromResult(new Uri(url));
    }
}

internal sealed class ObjectStorageHealthCheck(IAmazonS3 client, IOptions<ObjectStorageOptions> options)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetBucketLocationAsync(options.Value.Bucket, cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RustFS is not reachable or the bucket is missing.", ex);
        }
    }
}

public static class StorageExtensions
{
    public static IServiceCollection AddCracraStorage(this IServiceCollection services)
    {
        services.AddOptions<ObjectStorageOptions>()
            .BindConfiguration(ObjectStorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;

            return new AmazonS3Client(
                settings.AccessKey,
                settings.SecretKey,
                new AmazonS3Config
                {
                    ServiceURL = settings.ServiceUrl,
                    ForcePathStyle = settings.ForcePathStyle,
                    // RustFS is not a region-aware service; the SDK still insists on one being set.
                    AuthenticationRegion = "us-east-1",
                });
        });

        services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        services.AddHealthChecks()
            .AddCheck<ObjectStorageHealthCheck>("rustfs", tags: ["ready", "storage"]);

        return services;
    }
}
