using Stockly.Domain.Suppliers;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Lookups of the current shop's learned product aliases (tenant-filtered, read records only).</summary>
public interface IProductAliasRepository
{
    /// <summary>
    /// Aliases of <paramref name="supplierId"/> (<c>null</c> = unknown supplier) whose normalized text is one of
    /// <paramref name="normalizedAliases"/>, for non-archived products. One query.
    /// </summary>
    Task<IReadOnlyList<AliasMatch>> FindExactAsync(
        Guid? supplierId, IReadOnlyCollection<string> normalizedAliases, CancellationToken cancellationToken);

    /// <summary>
    /// Learns the aliases of a confirmed invoice in one statement (docs/design/backend.md §11 step 8): a new
    /// (shop, supplier, normalized alias) is inserted; an existing one points to the alias's product and unit, adds the
    /// alias's <c>TimesConfirmed</c> and gets its <c>LastUsedAt</c>. The aliases have distinct normalized texts and one
    /// supplier. Runs immediately; the products and units must already be saved.
    /// </summary>
    Task UpsertAsync(IReadOnlyList<ProductAlias> aliases, CancellationToken cancellationToken);
}

/// <summary>A learned alias: its normalized text, the product and the unit it stands for.</summary>
public sealed record AliasMatch(string NormalizedAlias, Guid ProductId, Guid ProductUnitId);
