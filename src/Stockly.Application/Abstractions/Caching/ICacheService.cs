namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// The read cache (docs/design/redis.md, AGENTS.md rules 21–25). Keys come from <see cref="CacheKeys"/>. Every method
/// degrades instead of failing: when Redis is down, reads miss, writes are skipped and a throttled warning is logged.
/// Never read a quantity from here to make a write decision (rule 24).
/// </summary>
public interface ICacheService
{
    /// <summary>The cached value, or <c>null</c> on a miss or when Redis is unavailable.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class;

    /// <summary>Stores <paramref name="value"/> with a time to live (every value key has one, rule 22).</summary>
    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
        where T : class;

    /// <summary>The current value of a version counter (0 when it was never bumped), or <c>null</c> when Redis is unavailable.</summary>
    Task<long?> GetVersionAsync(string key, CancellationToken cancellationToken);

    /// <summary>Bumps a version counter, so every key built from the old version becomes unreachable (rule 23).</summary>
    Task IncrementVersionAsync(string key, CancellationToken cancellationToken);
}
