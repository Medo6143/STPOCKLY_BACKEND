using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Sales;

namespace Stockly.Application.Features.Sales.GetSale;

/// <summary>S3: one sale with its lines. Another shop's sale is <c>Sale.NotFound</c> (AGENTS.md rule 10).</summary>
public sealed record GetSaleQuery(Guid SaleId) : IQuery<SaleDto>;
