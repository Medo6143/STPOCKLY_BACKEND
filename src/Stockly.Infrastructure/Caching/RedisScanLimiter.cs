using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Invoices;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// Per-shop scan rate limit and daily quota with atomic Lua scripts (docs/design/redis.md §1, backend.md §3.3). Every
/// counter gets its TTL in the same script as its first increment. Any Redis failure allows the scan (rate) or answers
/// <see cref="ScanQuotaResult.Unavailable"/> (quota) with a throttled warning: Redis never fails a request (rule 25).
/// </summary>
internal sealed partial class RedisScanLimiter(
    RedisConnection redis,
    IClock clock,
    ScanOptions options,
    RedisDegradationLog degradation,
    ILogger<RedisScanLimiter> logger) : IScanRateLimiter, IScanQuotaCounter
{
    private const int SecondsPerMinute = 60;

    // INCR; the first increment of the minute sets the TTL. Returns the count.
    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    // Refuses (-1) when the day's count already reached the quota; otherwise INCR with the TTL on the first one.
    private const string ConsumeScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current >= tonumber(ARGV[1]) then return -1 end
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[2]) end
        return count
        """;

    // DECR only a positive counter, so a release never creates a key or goes below zero.
    private const string ReleaseScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current > 0 then return redis.call('DECR', KEYS[1]) end
        return 0
        """;

    public async Task<RateLimitDecision> TryAcquireAsync(Guid shopId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            var count = (long)await database.ScriptEvaluateAsync(
                    IncrementScript,
                    [CacheKeys.ScanRate(shopId, now)],
                    [(long)CacheKeys.ScanRateTimeToLive.TotalSeconds])
                .WaitAsync(cancellationToken);
            if (count <= options.RateLimitPerMinute)
            {
                return RateLimitDecision.Allow;
            }

            LogRateLimited(logger, shopId, count);
            return new RateLimitDecision(false, Math.Max(1, SecondsPerMinute - now.UtcDateTime.Second));
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(nameof(TryAcquireAsync), exception);
            return RateLimitDecision.Allow;
        }
    }

    public async Task<ScanQuotaResult> TryConsumeAsync(Guid shopId, int dailyQuota, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            var count = (long)await database.ScriptEvaluateAsync(
                    ConsumeScript,
                    [CacheKeys.ScanQuota(shopId, clock.Today())],
                    [dailyQuota, (long)CacheKeys.ScanQuotaTimeToLive.TotalSeconds])
                .WaitAsync(cancellationToken);
            if (count >= 0)
            {
                return ScanQuotaResult.Allowed;
            }

            LogQuotaExceeded(logger, shopId, dailyQuota);
            return ScanQuotaResult.Exceeded;
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(nameof(TryConsumeAsync), exception);
            return ScanQuotaResult.Unavailable;
        }
    }

    public async Task ReleaseAsync(Guid shopId, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            await database.ScriptEvaluateAsync(ReleaseScript, [CacheKeys.ScanQuota(shopId, clock.Today())]).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(nameof(ReleaseAsync), exception);
        }
    }

    public async Task<bool> ResetAsync(Guid shopId, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            await database.KeyDeleteAsync(CacheKeys.ScanQuota(shopId, clock.Today())).WaitAsync(cancellationToken);
            LogQuotaReset(logger, shopId);
            return true;
        }
        catch (Exception exception) when (RedisDegradationLog.IsRedisFailure(exception))
        {
            degradation.Warn(nameof(ResetAsync), exception);
            return false;
        }
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Scan rate limit reached for shop {ShopId}: {Count} scans this minute")]
    private static partial void LogRateLimited(ILogger logger, Guid shopId, long count);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Daily scan quota used up for shop {ShopId} (quota {DailyQuota})")]
    private static partial void LogQuotaExceeded(ILogger logger, Guid shopId, int dailyQuota);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Daily scan counter of shop {ShopId} reset by the platform admin")]
    private static partial void LogQuotaReset(ILogger logger, Guid shopId);
}
