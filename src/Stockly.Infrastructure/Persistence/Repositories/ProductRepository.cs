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

    // ix_products_shop_id_normalized_name (shop_id, normalized_name) WHERE NOT is_archived, with normalized_name = ANY(@names);
    // units through the product_units (product_id) index, in the same statement.
    public async Task<IReadOnlyList<NamedProduct>> FindByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedNames);
        if (normalizedNames.Count == 0)
        {
            return [];
        }

        var names = normalizedNames.ToArray();
        return await db.Products.AsNoTracking()
            .Where(product => names.Contains(product.NormalizedName) && !product.IsArchived)
            .Select(product => new NamedProduct(
                product.Id,
                product.NormalizedName,
                product.Units.Select(unit => new NamedProductUnit(unit.Id, unit.Name, unit.IsBase)).ToList()))
            .ToListAsync(cancellationToken);
    }

    // Primary key = ANY(@ids); units through the product_units (product_id) index, in the same statement.
    public async Task<IReadOnlyList<ProductUnitsInfo>> FindUnitsInfoAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.ToArray();
        return await db.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .Select(product => new ProductUnitsInfo(product.Id, product.IsArchived, product.Units.Select(unit => unit.Id).ToList()))
            .ToListAsync(cancellationToken);
    }

    // Primary key of product_units.
    public Task<bool> UnitExistsAsync(Guid unitId, CancellationToken cancellationToken) =>
        db.Set<ProductUnit>().AsNoTracking().AnyAsync(unit => unit.Id == unitId, cancellationToken);
}
