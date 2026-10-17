using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Caching;

/// <summary>
/// The shared "Redis unavailable" warning of the scan limiter, the AI result cache and the status cache (AGENTS.md
/// rule 25, docs/design/redis.md §4): the operation name and exception type only (never a key value or a connection
/// string), at most once per <see cref="WarningInterval"/>. Singleton: the throttle is shared by every request and job.
/// </summary>
internal sealed partial class RedisDegradationLog(IClock clock, ILogger<RedisDegradationLog> logger)
{
    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private long _lastWarningTicks;

    /// <summary>A Redis outage or timeout. <c>RedisTimeoutException</c> derives from <see cref="TimeoutException"/>.</summary>
    public static bool IsRedisFailure(Exception exception) => exception is RedisException or TimeoutException;

    public void Warn(string operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var now = clock.UtcNow.UtcTicks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last < WarningInterval.Ticks || Interlocked.CompareExchange(ref _lastWarningTicks, now, last) != last)
        {
            return;
        }

        LogRedisUnavailable(logger, operation, exception.GetType().Name);
    }

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Redis unavailable during {Operation} ({ExceptionType}); degrading without it. Further warnings are suppressed for a minute.")]
    private static partial void LogRedisUnavailable(ILogger logger, string operation, string exceptionType);
}
