using Stockly.Domain.Common;

namespace Stockly.Domain.Products;

public static class ProductErrors
{
    public static readonly Error ShopRequired =
        Error.Validation("Product.ShopRequired", "A product must belong to a shop.");

    public static readonly Error NameRequired =
        Error.Validation("Product.NameRequired", "Product name is required.");

    public static readonly Error Archived =
        Error.Conflict("Product.Archived", "An archived product cannot be edited.");
}
