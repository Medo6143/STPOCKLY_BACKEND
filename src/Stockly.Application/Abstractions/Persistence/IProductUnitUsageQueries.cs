namespace Stockly.Application.Abstractions.Persistence;

/// <summary>
/// Whether lines of the current shop reference a unit (P8 <c>ProductUnit.InUse</c>, contract §10 item 16). Tenant-filtered,
/// one <c>EXISTS</c> query each.
/// </summary>
public interface IProductUnitUsageQueries
{
    Task<bool> IsUsedByInvoiceLinesAsync(Guid unitId, CancellationToken cancellationToken);

    Task<bool> IsUsedBySaleLinesAsync(Guid unitId, CancellationToken cancellationToken);
}
