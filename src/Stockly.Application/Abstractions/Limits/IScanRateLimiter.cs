namespace Stockly.Application.Abstractions.Limits;

/// <summary>
/// Per-shop scans per minute (<c>Scan:RateLimitPerMinute</c>, docs/design/redis.md). When Redis is unavailable the
/// limiter fails open: the request is allowed and a throttled warning is logged (AGENTS.md rule 25).
/// </summary>
public interface IScanRateLimiter
{
    Task<RateLimitDecision> TryAcquireAsync(Guid shopId, CancellationToken cancellationToken);
}

/// <summary>Whether the scan may run; when not, whole seconds until the next minute bucket (at least 1).</summary>
public sealed record RateLimitDecision(bool Allowed, int RetryAfterSeconds)
{
    public static RateLimitDecision Allow { get; } = new(true, 0);
}
