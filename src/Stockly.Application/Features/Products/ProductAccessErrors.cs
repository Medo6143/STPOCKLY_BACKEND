using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products;

/// <summary>Role rules of the products endpoints that depend on the request body, so they are checked in a handler.</summary>
public static class ProductAccessErrors
{
    /// <summary>Contract P3 (v1.1 decision 9): only the Owner may set <c>openingQuantity</c>.</summary>
    public static readonly Error OpeningStockOwnerOnly =
        Error.Forbidden("Product.OpeningStockOwnerOnly", "Only the shop owner may set the opening stock.");
}
