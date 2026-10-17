using System.Net;
using System.Runtime.InteropServices;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Storage;

namespace Stockly.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> on the S3 API (MinIO, ADR-0008). Objects are private; the real content type is stored on
/// the object. Logs keys and sizes only, never the bytes (AGENTS.md rule 12).
/// </summary>
internal sealed partial class S3FileStorage(IAmazonS3 s3, StorageOptions options, ILogger<S3FileStorage> logger) : IFileStorage
{
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

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        s3.DeleteObjectAsync(options.Bucket, key, cancellationToken);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Stored image {ObjectKey} ({SizeBytes} bytes, {ContentType})")]
    private static partial void LogStored(ILogger logger, string objectKey, int sizeBytes, string contentType);

    [LoggerMessage(EventId = 31, Level = LogLevel.Warning, Message = "Image {ObjectKey} is missing from storage")]
    private static partial void LogMissing(ILogger logger, string objectKey);
}
