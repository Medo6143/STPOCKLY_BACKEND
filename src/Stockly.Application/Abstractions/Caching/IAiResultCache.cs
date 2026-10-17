using Stockly.Application.Abstractions.Ai;

namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// Validated AI extractions by image hash, per shop (<c>stockly:{shopId}:ai:invoice:{sha256}</c>, 7 days). A value
/// written with another prompt version is a miss. Redis failures are misses and skipped writes, never errors (rule 25).
/// </summary>
public interface IAiResultCache
{
    Task<CachedExtraction?> GetAsync(Guid shopId, string imageSha256, CancellationToken cancellationToken);

    /// <summary>Stores a validated extraction and the model that produced it, stamped with the current prompt version.</summary>
    Task SetAsync(Guid shopId, string imageSha256, string model, ExtractedInvoice extraction, CancellationToken cancellationToken);
}

/// <summary>A cached extraction: the prompt version it was made with, the model, and the validated output.</summary>
public sealed record CachedExtraction(string PromptVersion, string Model, ExtractedInvoice Extraction);
