using Stockly.Domain.Products;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Writes to the current shop's products (tenant-filtered). Stock changes go through <c>IStockService</c>.</summary>
public interface IProductRepository
{
    void Add(Product product);

    /// <summary>The product with its units, tracked for an edit; <c>null</c> when missing or another shop's.</summary>
    Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Whether another non-archived product of the shop has this (trimmed) barcode.</summary>
    Task<bool> IsBarcodeUsedAsync(string barcode, Guid? exceptProductId, CancellationToken cancellationToken);

    /// <summary>Whether another unit of any product of the shop (archived ones included) has this (trimmed) barcode.</summary>
    Task<bool> IsUnitBarcodeUsedAsync(string barcode, Guid? exceptUnitId, CancellationToken cancellationToken);
}
