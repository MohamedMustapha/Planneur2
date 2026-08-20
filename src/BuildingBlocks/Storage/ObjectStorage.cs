using System.ComponentModel.DataAnnotations;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Creates the bucket at startup when it is missing.
    /// </summary>
    /// <remarks>
    /// On in every real deployment, where a first run against an empty RustFS should just work. Off in the
    /// integration tests, which substitute the storage entirely — leaving it on there costs every host start the
    /// AWS SDK's full retry budget against a port nothing is listening on, which is seconds each across a suite
    /// that starts a hundred hosts.
    /// </remarks>
    public bool CreateBucketOnStartup { get; set; } = true;
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

    /// <summary>
    /// Whether the payload may go up unsigned.
    /// </summary>
    /// <remarks>
    /// Only over HTTPS. Unsigned payloads shift integrity from the signature to the transport, so the AWS SDK
    /// refuses the combination over plain HTTP — with an exception at sign time rather than a rejected upload,
    /// which is why nothing caught it until a slice actually stored something. On-prem RustFS is commonly plain
    /// HTTP inside the perimeter, and there the payload is simply signed instead.
    /// </remarks>
    private readonly bool _canSkipPayloadSigning =
        options.Value.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        => await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                DisablePayloadSigning = _canSkipPayloadSigning,
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

/// <summary>
/// Creates the bucket on startup if it is not there.
/// </summary>
/// <remarks>
/// <para>
/// S0 wired storage up and nothing wrote to it, so a missing bucket showed only as an unhealthy probe. S8 is the
/// first slice that stores anything — an exported report — and "the bucket does not exist" is a first-run
/// condition rather than an operator error worth failing over.
/// </para>
/// <para>
/// Never fails startup. A storage backend that is down at boot must not take the API with it: activity logging,
/// the boards and every report except its export work perfectly well without it, and the health check already
/// says so in a way an operator can alert on.
/// </para>
/// </remarks>
internal sealed class ObjectStorageInitializer(
    IAmazonS3 client,
    IOptions<ObjectStorageOptions> options,
    ILogger<ObjectStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.CreateBucketOnStartup)
        {
            return;
        }

        var bucket = options.Value.Bucket;

        try
        {
            if (await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
            {
                return;
            }

            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);

            logger.LogInformation("Created object storage bucket {Bucket}", bucket);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not ensure object storage bucket {Bucket}. Exports will fail until it exists.",
                bucket);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

        services.AddHostedService<ObjectStorageInitializer>();

        services.AddHealthChecks()
            .AddCheck<ObjectStorageHealthCheck>("rustfs", tags: ["ready", "storage"]);

        return services;
    }
}
