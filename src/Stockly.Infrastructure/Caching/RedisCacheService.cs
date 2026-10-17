using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// <see cref="ICacheService"/> over <see cref="IDistributedCache"/> (values, System.Text.Json DTOs with a TTL) and
/// <see cref="IConnectionMultiplexer"/> (version counters: <c>GET</c>/<c>INCR</c>, which <see cref="IDistributedCache"/>
/// cannot do). A Redis failure never fails a request (AGENTS.md rule 25): reads miss, writes are skipped, and a warning
/// with the operation and the exception type (never a key value or a connection string) is logged at most once per
/// <see cref="WarningInterval"/>. Singleton: the throttle is shared by every request.
/// </summary>
internal sealed partial class RedisCacheService(
    IDistributedCache cache,
    RedisConnection redis,
    IClock clock,
    ILogger<RedisCacheService> logger) : ICacheService
{
    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private long _lastWarningTicks;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception exception) when (IsRedisFailure(exception) || exception is JsonException)
        {
            Warn(nameof(GetAsync), exception);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive };
            await cache.SetAsync(key, bytes, options, cancellationToken);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            Warn(nameof(SetAsync), exception);
        }
    }

    public async Task<long?> GetVersionAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            var value = await database.StringGetAsync(key).WaitAsync(cancellationToken);
            return value.IsNull ? 0 : long.Parse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (IsRedisFailure(exception) || exception is FormatException or OverflowException)
        {
            Warn(nameof(GetVersionAsync), exception);
            return null;
        }
    }

    public async Task IncrementVersionAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var database = await redis.GetDatabaseAsync(cancellationToken);
            await database.StringIncrementAsync(key).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            Warn(nameof(IncrementVersionAsync), exception);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            Warn(nameof(RemoveAsync), exception);
        }
    }

    // RedisTimeoutException derives from TimeoutException, not RedisException.
    private static bool IsRedisFailure(Exception exception) => exception is RedisException or TimeoutException;

    private void Warn(string operation, Exception exception)
    {
        var now = clock.UtcNow.UtcTicks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last < WarningInterval.Ticks || Interlocked.CompareExchange(ref _lastWarningTicks, now, last) != last)
        {
            return;
        }

        LogRedisUnavailable(logger, operation, exception.GetType().Name);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Redis cache unavailable during {Operation} ({ExceptionType}); serving from PostgreSQL. Further warnings are suppressed for a minute.")]
    private static partial void LogRedisUnavailable(ILogger logger, string operation, string exceptionType);
}
