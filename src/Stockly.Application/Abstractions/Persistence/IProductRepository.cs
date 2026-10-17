using Stockly.Domain.Products;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Writes to the current shop's products (tenant-filtered). Stock changes go through <c>IStockService</c>.</summary>
public interface IProductRepository
{
    void Add(Product product);

    /// <summary>The product with its units, tracked for an edit; <c>null</c> when missing or another shop's.</summary>
    Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Whether another non-archived product of the shop has this (trimmed) barcode.</summary>
    Task<bool> IsBarcodeUsedAsync(string barcode, Guid? exceptProductId, CancellationToken cancellationToken);

    /// <summary>Whether another unit of any product of the shop (archived ones included) has this (trimmed) barcode.</summary>
    Task<bool> IsUnitBarcodeUsedAsync(string barcode, Guid? exceptUnitId, CancellationToken cancellationToken);

    /// <summary>
    /// Non-archived products whose normalized name is one of <paramref name="normalizedNames"/>, with their units, as
    /// read records (matching v1). One query.
    /// </summary>
    Task<IReadOnlyList<NamedProduct>> FindByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);

    /// <summary>
    /// The products with these ids and the ids of their units, archived ones included, as read records (review edit,
    /// I4). Missing and other shops' ids are absent. One query.
    /// </summary>
    Task<IReadOnlyList<ProductUnitsInfo>> FindUnitsInfoAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>Whether a unit with this id exists on any product of the shop.</summary>
    Task<bool> UnitExistsAsync(Guid unitId, CancellationToken cancellationToken);
}

/// <summary>A product found by its normalized name, with its units.</summary>
public sealed record NamedProduct(Guid Id, string NormalizedName, IReadOnlyList<NamedProductUnit> Units);

/// <summary>A unit of a <see cref="NamedProduct"/>: its name as stored and whether it is the base unit.</summary>
public sealed record NamedProductUnit(Guid Id, string Name, bool IsBase);

/// <summary>A product's archived flag and unit ids, for the review edit's checks.</summary>
public sealed record ProductUnitsInfo(Guid Id, bool IsArchived, IReadOnlyList<Guid> UnitIds);
