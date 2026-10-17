using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Common;
using Stockly.Contracts.Sales;

namespace Stockly.Application.Features.Sales.GetSales;

/// <summary>
/// S2: a page of the shop's sales, newest first, created at or after <see cref="From"/> and before <see cref="To"/> when
/// given.
/// </summary>
public sealed record GetSalesQuery(DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize)
    : IQuery<PagedResponse<SaleListItem>>;
