namespace Stockly.Application.Abstractions.Storage;

/// <summary>
/// Private object storage for invoice images (ADR-0008, MinIO in development). Keys come from
/// <c>InvoiceImageKeys</c>. Failures other than "object missing" throw.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/> with its real content type.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>The object, or <c>null</c> when it does not exist.</summary>
    Task<StoredFile?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes the object; a missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>A stored object's bytes and content type.</summary>
public sealed record StoredFile(ReadOnlyMemory<byte> Content, string ContentType);
