using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Read-only projections of the current shop's catalog (tenant-filtered, no tracking, AGENTS.md rule 33).</summary>
public interface IProductQueries
{
    /// <summary>A page of products sorted by normalized name (contract P1).</summary>
    Task<PagedResponse<ProductListItem>> ListAsync(ProductListFilter filter, CancellationToken cancellationToken);

    /// <summary>The product with its units; <c>null</c> when missing or another shop's.</summary>
    Task<ProductDetail?> FindDetailAsync(Guid productId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>A page of the product's movements, newest first (contract P11).</summary>
    Task<PagedResponse<StockMovementDto>> ListMovementsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// P1 filter. <paramref name="NormalizedSearch"/> is already Arabic-normalized (<c>null</c> = no search);
/// <paramref name="LowStockOnly"/> keeps <c>quantity &lt;= low_stock_threshold</c>.
/// </summary>
public sealed record ProductListFilter(string? NormalizedSearch, bool LowStockOnly, bool IncludeArchived, int Page, int PageSize);
