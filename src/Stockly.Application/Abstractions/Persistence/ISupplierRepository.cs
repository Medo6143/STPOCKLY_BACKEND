namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Supplier lookups for the current shop (tenant-filtered).</summary>
public interface ISupplierRepository
{
    /// <summary>The non-archived supplier with this Arabic-normalized name; <c>null</c> when none.</summary>
    Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);
}
