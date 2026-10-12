namespace Stockly.Api.RateLimiting;

/// <summary>
/// Per-IP limit on login and register (contract §1.4, v1.4): <see cref="PermitLimit"/> requests per fixed window of
/// <see cref="WindowSeconds"/> seconds, per endpoint. Settings <c>RateLimiting__Auth__PermitLimit</c> and
/// <c>RateLimiting__Auth__WindowSeconds</c>; both optional, positive integers.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";
    public const int DefaultPermitLimit = 10;
    public const int DefaultWindowSeconds = 60;

    public int PermitLimit { get; init; } = DefaultPermitLimit;

    public int WindowSeconds { get; init; } = DefaultWindowSeconds;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
