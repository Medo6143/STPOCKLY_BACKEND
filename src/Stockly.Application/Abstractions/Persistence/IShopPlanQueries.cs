namespace Stockly.Application.Abstractions.Persistence;

/// <summary>The current shop's plan limits (tenant-filtered subscription joined to its global plan).</summary>
public interface IShopPlanQueries
{
    /// <summary>
    /// Scans per Cairo day of the active subscription: its admin override when set, else its plan's quota (contract
    /// <c>effectiveDailyScanQuota</c>); <c>null</c> when the shop has no active subscription. Read from PostgreSQL on every
    /// scan (not cached), so an override takes effect on the next scan.
    /// </summary>
    Task<int?> GetDailyScanQuotaAsync(CancellationToken cancellationToken);
}
