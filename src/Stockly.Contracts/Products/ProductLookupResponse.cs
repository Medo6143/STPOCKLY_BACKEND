namespace Stockly.Contracts.Products;

/// <summary>
/// P12 <c>GET /api/products/lookup?barcode=</c>: the non-archived product with this barcode and the unit it names (the
/// unit whose barcode matched, or the base unit for a product barcode).
/// </summary>
public sealed record ProductLookupResponse(ProductListItem Product, UnitDto Unit);
