namespace Stockly.Application.Abstractions.Persistence;

/// <summary>The current shop's plan limits (tenant-filtered subscription joined to its global plan).</summary>
public interface IShopPlanQueries
{
    /// <summary>Scans per Cairo day of the active subscription's plan; <c>null</c> when the shop has no active subscription.</summary>
    Task<int?> GetDailyScanQuotaAsync(CancellationToken cancellationToken);
}
