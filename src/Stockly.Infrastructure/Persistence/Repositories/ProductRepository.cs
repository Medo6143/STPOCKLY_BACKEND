using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Products;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Product writes. Every query runs under the tenant filter (ADR-0002).</summary>
internal sealed class ProductRepository(StocklyDbContext db) : IProductRepository
{
    public void Add(Product product) => db.Products.Add(product);

    public Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        db.Products
            .Include(product => product.Units)
            .SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

    // Served by ux_products_shop_id_barcode (shop_id, barcode) WHERE barcode IS NOT NULL AND NOT is_archived.
    public Task<bool> IsBarcodeUsedAsync(string barcode, Guid? exceptProductId, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().AnyAsync(
            product => product.Barcode == barcode && !product.IsArchived && product.Id != exceptProductId,
            cancellationToken);

    // Served by ux_product_units_shop_id_barcode (shop_id, barcode) WHERE barcode IS NOT NULL.
    public Task<bool> IsUnitBarcodeUsedAsync(string barcode, Guid? exceptUnitId, CancellationToken cancellationToken) =>
        db.Set<ProductUnit>().AsNoTracking().AnyAsync(
            unit => unit.Barcode == barcode && unit.Id != exceptUnitId,
            cancellationToken);
}
