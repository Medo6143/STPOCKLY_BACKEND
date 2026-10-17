using System.ComponentModel.DataAnnotations;

namespace Stockly.Infrastructure.Storage;

/// <summary>
/// Configuration section <c>Storage</c> (docs/design/backend.md §14): the S3 endpoint (MinIO in development, ADR-0008)
/// and the least-privilege API user. The keys are secrets from <c>.env</c> (AGENTS.md rule 11). Validated at startup.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public Uri? Endpoint { get; set; }

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>The AWS SDK needs a region to sign requests; MinIO ignores it.</summary>
    [Required]
    public string Region { get; set; } = "us-east-1";

    /// <summary>Path-style URLs (<c>endpoint/bucket/key</c>), required by MinIO.</summary>
    public bool ForcePathStyle { get; set; } = true;
}
