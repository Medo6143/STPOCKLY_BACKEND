using System.Net;
using System.Runtime.InteropServices;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> on the S3 API (MinIO, ADR-0008). Objects are private; the real content type is stored on
/// the object. Logs keys and sizes only, never the bytes (AGENTS.md rule 12).
/// </summary>
internal sealed partial class S3FileStorage(IAmazonS3 s3, StorageOptions options, IClock clock, ILogger<S3FileStorage> logger) : IFileStorage
{
    private const string Http = "http";

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = MemoryMarshal.TryGetArray(content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);
        var request = new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        await s3.PutObjectAsync(request, cancellationToken);
        LogStored(logger, key, content.Length, contentType);
    }

    public async Task<StoredFile?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3.GetObjectAsync(options.Bucket, key, cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return new StoredFile(buffer.ToArray(), response.Headers.ContentType);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogMissing(logger, key);
            return null;
        }
    }

    // A HEAD first: a pre-signed URL can be made for any key, but the contract wants null for a missing object (AD18).
    // The URL is signed for the configured endpoint, with its scheme.
    public async Task<string?> GetDownloadUrlAsync(string key, TimeSpan validFor, CancellationToken cancellationToken)
    {
        try
        {
            await s3.GetObjectMetadataAsync(options.Bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogMissing(logger, key);
            return null;
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = (clock.UtcNow + validFor).UtcDateTime,
            Protocol = options.Endpoint?.Scheme == Http ? Protocol.HTTP : Protocol.HTTPS,
        };
        return await s3.GetPreSignedURLAsync(request);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        s3.DeleteObjectAsync(options.Bucket, key, cancellationToken);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Stored image {ObjectKey} ({SizeBytes} bytes, {ContentType})")]
    private static partial void LogStored(ILogger logger, string objectKey, int sizeBytes, string contentType);

    [LoggerMessage(EventId = 31, Level = LogLevel.Warning, Message = "Image {ObjectKey} is missing from storage")]
    private static partial void LogMissing(ILogger logger, string objectKey);
}
