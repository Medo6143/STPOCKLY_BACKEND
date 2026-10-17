using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Sales;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Sale writes; the tenant interceptor checks the shop on save (ADR-0002).</summary>
internal sealed class SaleRepository(StocklyDbContext db) : ISaleRepository
{
    public void Add(Sale sale) => db.Set<Sale>().Add(sale);
}
