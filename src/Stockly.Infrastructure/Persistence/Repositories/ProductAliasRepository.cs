using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Learned alias lookups for matching (tenant filter, no tracking, read records). Indexes in docs/erd.md §3.2.</summary>
internal sealed class ProductAliasRepository(StocklyDbContext db) : IProductAliasRepository
{
    // Arbiter: ux_product_aliases_shop_id_supplier_id_normalized_alias (NULLS NOT DISTINCT, so a null supplier conflicts).
    // One row per alias from parallel arrays; an existing alias adds the new confirmations.
    private const string UpsertSql =
        "INSERT INTO product_aliases (id, shop_id, product_id, supplier_id, product_unit_id, alias_text, normalized_alias, " +
        "times_confirmed, last_used_at) " +
        "SELECT a.id, a.shop_id, a.product_id, {0}, a.product_unit_id, a.alias_text, a.normalized_alias, a.times_confirmed, a.last_used_at " +
        "FROM unnest({1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}) " +
        "AS a(id, shop_id, product_id, product_unit_id, alias_text, normalized_alias, times_confirmed, last_used_at) " +
        "ON CONFLICT (shop_id, supplier_id, normalized_alias) DO UPDATE SET " +
        "product_id = EXCLUDED.product_id, product_unit_id = EXCLUDED.product_unit_id, " +
        "times_confirmed = product_aliases.times_confirmed + EXCLUDED.times_confirmed, last_used_at = EXCLUDED.last_used_at";

    // ux_product_aliases_shop_id_supplier_id_normalized_alias: shop_id = @shop AND supplier_id = @s (IS NULL when unknown)
    // AND normalized_alias = ANY(@names), joined to the non-archived product by primary key. One statement.
    public async Task<IReadOnlyList<AliasMatch>> FindExactAsync(
        Guid? supplierId, IReadOnlyCollection<string> normalizedAliases, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedAliases);
        if (normalizedAliases.Count == 0)
        {
            return [];
        }

        var names = normalizedAliases.ToArray();
        var aliases = db.Set<ProductAlias>().AsNoTracking().Where(alias => names.Contains(alias.NormalizedAlias));
        aliases = supplierId is { } supplier
            ? aliases.Where(alias => alias.SupplierId == supplier)
            : aliases.Where(alias => alias.SupplierId == null);

        return await aliases
            .Join(
                db.Products.AsNoTracking().Where(product => !product.IsArchived),
                alias => alias.ProductId,
                product => product.Id,
                (alias, product) => new AliasMatch(alias.NormalizedAlias, alias.ProductId, alias.ProductUnitId))
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(IReadOnlyList<ProductAlias> aliases, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (aliases.Count == 0)
        {
            return;
        }

        var supplier = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)aliases[0].SupplierId ?? DBNull.Value };
        object[] parameters =
        [
            supplier,
            aliases.Select(alias => alias.Id).ToArray(),
            aliases.Select(alias => alias.ShopId).ToArray(),
            aliases.Select(alias => alias.ProductId).ToArray(),
            aliases.Select(alias => alias.ProductUnitId).ToArray(),
            aliases.Select(alias => alias.AliasText).ToArray(),
            aliases.Select(alias => alias.NormalizedAlias).ToArray(),
            aliases.Select(alias => alias.TimesConfirmed).ToArray(),
            aliases.Select(alias => alias.LastUsedAt).ToArray(),
        ];
        await db.Database.ExecuteSqlRawAsync(UpsertSql, parameters, cancellationToken);
    }
}
