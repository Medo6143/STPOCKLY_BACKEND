using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.GetProducts;

/// <summary>P1 without <c>lowStock</c>: a page of products sorted by normalized name, cached 10 minutes per catalog version.</summary>
public sealed record GetProductsQuery(string? Search, bool IncludeArchived, int Page, int PageSize)
    : IQuery<PagedResponse<ProductListItem>>, IProductListQuery;
