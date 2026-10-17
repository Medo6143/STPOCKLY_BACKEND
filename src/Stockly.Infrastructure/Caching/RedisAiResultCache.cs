using System.Text.Json;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Infrastructure.Ai.OpenRouter.Prompts;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// <see cref="IAiResultCache"/>: <c>stockly:{shopId}:ai:invoice:{sha256}</c>, 7 days. Values carry the prompt version
/// (<see cref="InvoiceExtractionPrompt.Version"/>); a value from another version is a miss, so a prompt change never
/// serves stale extractions. Redis failures and unreadable values are misses (rule 25).
/// </summary>
internal sealed class RedisAiResultCache(RedisConnection redis, RedisDegradationLog degradation) : IAiResultCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CachedExtraction?> GetAsync(Guid shopId, string imageSha256, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            var value = await database.StringGetAsync(CacheKeys.AiInvoice(shopId, imageSha256)).WaitAsync(cancellationToken);
            var cached = value.IsNull ? null : JsonSerializer.Deserialize<CachedExtraction>(value.ToString(), Json);
            return cached?.PromptVersion == InvoiceExtractionPrompt.Version ? cached : null;
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception) || exception is JsonException)
        {
            degradation.Warn(nameof(GetAsync), exception);
            return null;
        }
    }

    public async Task SetAsync(
        Guid shopId, string imageSha256, string model, ExtractedInvoice extraction, CancellationToken cancellationToken)
    {
        try
        {
            var value = JsonSerializer.Serialize(new CachedExtraction(InvoiceExtractionPrompt.Version, model, extraction), Json);
            var database = await redis.GetDatabaseAsync(cancellationToken);
            await database.StringSetAsync(CacheKeys.AiInvoice(shopId, imageSha256), value, CacheKeys.AiResultTimeToLive)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(nameof(SetAsync), exception);
        }
    }
}
