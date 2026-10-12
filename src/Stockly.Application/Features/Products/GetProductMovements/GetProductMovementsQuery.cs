using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.GetProductMovements;

/// <summary>P11: a page of the product's stock movements, newest first.</summary>
public sealed record GetProductMovementsQuery(Guid ProductId, int Page, int PageSize) : IQuery<PagedResponse<StockMovementDto>>;
