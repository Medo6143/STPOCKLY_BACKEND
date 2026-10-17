using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class SupplierRepository(StocklyDbContext db) : ISupplierRepository
{
    // ux_suppliers_shop_id_normalized_name (shop_id, normalized_name) WHERE NOT is_archived.
    public Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken) =>
        db.Set<Supplier>().AsNoTracking()
            .Where(supplier => supplier.NormalizedName == normalizedName && !supplier.IsArchived)
            .Select(supplier => (Guid?)supplier.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
