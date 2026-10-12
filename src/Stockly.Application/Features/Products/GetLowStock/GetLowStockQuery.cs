using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.GetLowStock;

/// <summary>
/// P1 with <c>lowStock=true</c>: only products with <c>quantity &lt;= lowStockThreshold</c> (partial index
/// <c>ix_products_low_stock</c>), sorted by normalized name, cached 5 minutes per catalog version.
/// </summary>
public sealed record GetLowStockQuery(string? Search, bool IncludeArchived, int Page, int PageSize)
    : IQuery<PagedResponse<ProductListItem>>, IProductListQuery;
