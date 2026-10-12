using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>
/// Catalog reads: projections, no tracking, tenant-filtered (AGENTS.md rule 33). The indexes each query uses are
/// listed in docs/erd.md §3.2 and §3.5.
/// </summary>
internal sealed class ProductQueries(StocklyDbContext db) : IProductQueries
{
    private const string LikeEscape = "\\";

    public async Task<PagedResponse<ProductListItem>> ListAsync(ProductListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var products = db.Products.AsNoTracking();
        if (!filter.IncludeArchived)
        {
            products = products.Where(product => !product.IsArchived);
        }

        // ix_products_low_stock: (shop_id) WHERE quantity <= low_stock_threshold AND NOT is_archived.
        if (filter.LowStockOnly)
        {
            products = products.Where(product => product.Quantity <= product.LowStockThreshold);
        }

        // ix_products_normalized_name_trgm (GIN, gin_trgm_ops) serves ILIKE '%term%'.
        if (filter.NormalizedSearch is { } search)
        {
            var pattern = "%" + EscapeLike(search) + "%";
            products = products.Where(product => EF.Functions.ILike(product.NormalizedName, pattern, LikeEscape));
        }

        var totalCount = await products.CountAsync(cancellationToken);

        // ix_products_shop_id_normalized_name: (shop_id, normalized_name) WHERE NOT is_archived; id breaks ties.
        var rows = await products
            .OrderBy(product => product.NormalizedName)
            .ThenBy(product => product.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Quantity,
                product.LowStockThreshold,
                product.IsArchived,
                BaseUnit = product.Units
                    .Where(unit => unit.IsBase)
                    .Select(unit => new { unit.Id, unit.Name, unit.SellPrice })
                    .First(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ProductListItem(
                row.Id,
                row.Name,
                ProductMapping.Quantity(row.Quantity),
                new BaseUnitSummary(row.BaseUnit.Id, row.BaseUnit.Name, ProductMapping.Money(row.BaseUnit.SellPrice)),
                ProductMapping.Quantity(row.LowStockThreshold),
                row.Quantity <= row.LowStockThreshold,
                row.IsArchived))
            .ToList();
        return new PagedResponse<ProductListItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<ProductDetail?> FindDetailAsync(Guid productId, CancellationToken cancellationToken)
    {
        var row = await db.Products.AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Barcode,
                product.Category,
                product.Quantity,
                product.CostPrice,
                product.LowStockThreshold,
                product.IsArchived,
                product.CreatedAt,
                UpdatedAt = EF.Property<DateTimeOffset>(product, ShadowProperties.UpdatedAt),
                Units = product.Units
                    .OrderByDescending(unit => unit.IsBase)
                    .ThenBy(unit => unit.Factor)
                    .ThenBy(unit => unit.Name)
                    .Select(unit => new { unit.Id, unit.Name, unit.Factor, unit.SellPrice, unit.Barcode, unit.IsBase })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ProductDetail(
                row.Id,
                row.Name,
                row.Barcode,
                row.Category,
                ProductMapping.Quantity(row.Quantity),
                ProductMapping.Money(row.CostPrice),
                ProductMapping.Quantity(row.LowStockThreshold),
                row.Quantity <= row.LowStockThreshold,
                row.IsArchived,
                row.Units
                    .Select(unit => new UnitDto(
                        unit.Id,
                        unit.Name,
                        ProductMapping.Factor(unit.Factor),
                        ProductMapping.Money(unit.SellPrice),
                        unit.Barcode,
                        unit.IsBase))
                    .ToList(),
                row.CreatedAt.UtcDateTime,
                row.UpdatedAt.UtcDateTime);
    }

    public Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken);

    public async Task<PagedResponse<StockMovementDto>> ListMovementsAsync(
        Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // ix_stock_movements_shop_id_product_id_created_at: (shop_id, product_id, created_at DESC).
        var movements = db.StockMovements.AsNoTracking().Where(movement => movement.ProductId == productId);

        var totalCount = await movements.CountAsync(cancellationToken);
        var rows = await movements
            .OrderByDescending(movement => movement.CreatedAt)
            .ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movement => new
            {
                movement.Id,
                movement.ProductId,
                movement.QuantityChange,
                movement.BalanceAfter,
                movement.Reason,
                movement.InvoiceId,
                movement.SaleId,
                movement.Note,
                movement.CreatedByUserId,
                movement.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new StockMovementDto(
                row.Id,
                row.ProductId,
                ProductMapping.Quantity(row.QuantityChange),
                ProductMapping.Quantity(row.BalanceAfter),
                ProductMapping.ToContract(row.Reason),
                row.InvoiceId,
                row.SaleId,
                row.Note,
                row.CreatedByUserId,
                row.CreatedAt.UtcDateTime))
            .ToList();
        return new PagedResponse<StockMovementDto>(items, page, pageSize, totalCount);
    }

    // The term is matched literally: LIKE wildcards and the escape character itself are escaped.
    private static string EscapeLike(string term) =>
        term.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
