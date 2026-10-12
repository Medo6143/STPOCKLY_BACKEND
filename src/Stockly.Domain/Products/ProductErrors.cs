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

    /// <summary>No such product in the current shop. Also returned for another shop's product (AGENTS.md rule 10).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Product.NotFound", "The product was not found.");

    /// <summary>The barcode is used by another non-archived product of the shop (checked against the database).</summary>
    public static readonly Error DuplicateBarcode =
        Error.Conflict("Product.DuplicateBarcode", "Another active product of this shop already has this barcode.");

    /// <summary>Request validators reject a longer value first (<c>Validation.Failed</c>); this guards the invariant.</summary>
    public static readonly Error BarcodeTooLong =
        Error.Validation("Product.BarcodeTooLong", $"A product barcode has at most {Product.BarcodeMaxLength} characters.");

    /// <summary>Request validators reject a longer value first (<c>Validation.Failed</c>); this guards the invariant.</summary>
    public static readonly Error CategoryTooLong =
        Error.Validation("Product.CategoryTooLong", $"A product category has at most {Product.CategoryMaxLength} characters.");
}
