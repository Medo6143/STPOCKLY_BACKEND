using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.LookupProduct;

/// <summary>P12: the non-archived product with this product or unit barcode (exact match), for the quick-sale screen.</summary>
public sealed record LookupProductQuery(string? Barcode) : IQuery<ProductLookupResponse>;
