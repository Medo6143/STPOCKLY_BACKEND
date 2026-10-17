namespace Stockly.Application.Abstractions.Limits;

/// <summary>
/// The plan's daily scan quota per shop, counted per Africa/Cairo day (docs/design/redis.md). When Redis is unavailable
/// it answers <see cref="ScanQuotaResult.Unavailable"/> and the caller counts today's invoices in PostgreSQL instead.
/// </summary>
public interface IScanQuotaCounter
{
    /// <summary>Takes one scan from today's quota unless it is used up.</summary>
    Task<ScanQuotaResult> TryConsumeAsync(Guid shopId, int dailyQuota, CancellationToken cancellationToken);

    /// <summary>Gives back a scan taken by <see cref="TryConsumeAsync"/> (upload failed, or a concurrent repeat won).</summary>
    Task ReleaseAsync(Guid shopId, CancellationToken cancellationToken);
}

public enum ScanQuotaResult
{
    Allowed = 1,
    Exceeded = 2,

    /// <summary>Redis could not be reached; nothing was counted.</summary>
    Unavailable = 3,
}
