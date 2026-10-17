using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class SupplierRepository(StocklyDbContext db) : ISupplierRepository
{
    // Arbiter: ux_suppliers_shop_id_normalized_name (shop_id, normalized_name) WHERE NOT is_archived.
    private const string InsertIfAbsentSql =
        "INSERT INTO suppliers (id, shop_id, name, normalized_name, phone, is_archived, created_at) " +
        "VALUES ({0}, {1}, {2}, {3}, {4}, false, {5}) " +
        "ON CONFLICT (shop_id, normalized_name) WHERE NOT is_archived DO NOTHING";

    // ux_suppliers_shop_id_normalized_name (shop_id, normalized_name) WHERE NOT is_archived.
    public Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken) =>
        db.Set<Supplier>().AsNoTracking()
            .Where(supplier => supplier.NormalizedName == normalizedName && !supplier.IsArchived)
            .Select(supplier => (Guid?)supplier.Id)
            .SingleOrDefaultAsync(cancellationToken);

    // A concurrent insert of the same name blocks on the unique index until the other transaction ends; DO NOTHING then
    // inserts nothing and the select (a new statement under READ COMMITTED) sees the committed row.
    public async Task<SupplierResolution> FindOrCreateAsync(Supplier candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var inserted = await db.Database.ExecuteSqlRawAsync(
            InsertIfAbsentSql,
            [
                candidate.Id,
                candidate.ShopId,
                candidate.Name,
                candidate.NormalizedName,
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)candidate.Phone ?? DBNull.Value },
                candidate.CreatedAt,
            ],
            cancellationToken);
        var supplierId = await FindIdByNormalizedNameAsync(candidate.NormalizedName, cancellationToken)
            ?? throw new InvalidOperationException("The supplier inserted or found in this transaction is not visible.");
        return new SupplierResolution(supplierId, Created: inserted == 1);
    }
}
