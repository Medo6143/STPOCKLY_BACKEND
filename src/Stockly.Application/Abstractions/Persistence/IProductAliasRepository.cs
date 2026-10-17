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
}

/// <summary>A learned alias: its normalized text, the product and the unit it stands for.</summary>
public sealed record AliasMatch(string NormalizedAlias, Guid ProductId, Guid ProductUnitId);
