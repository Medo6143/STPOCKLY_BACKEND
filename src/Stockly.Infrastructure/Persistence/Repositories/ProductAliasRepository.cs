using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Learned alias lookups for matching (tenant filter, no tracking, read records). Indexes in docs/erd.md §3.2.</summary>
internal sealed class ProductAliasRepository(StocklyDbContext db) : IProductAliasRepository
{
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
}
