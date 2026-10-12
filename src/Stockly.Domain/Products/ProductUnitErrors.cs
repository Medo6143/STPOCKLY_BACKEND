using Stockly.Domain.Common;

namespace Stockly.Domain.Products;

public static class ProductUnitErrors
{
    public static readonly Error NameRequired =
        Error.Validation("ProductUnit.NameRequired", "Unit name is required.");

    public static readonly Error DuplicateName =
        Error.Conflict("ProductUnit.DuplicateName", "This product already has a unit with this name.");

    public static readonly Error DuplicateBarcode =
        Error.Conflict("ProductUnit.DuplicateBarcode", "This product already has a unit with this barcode.");

    public static readonly Error NotFound =
        Error.NotFound("ProductUnit.NotFound", "The unit was not found on this product.");

    public static readonly Error BaseUnitCannotBeRemoved =
        Error.Validation("ProductUnit.BaseUnitCannotBeRemoved", "The base unit cannot be removed.");

    public static readonly Error BaseUnitFactorFixed =
        Error.Validation("ProductUnit.BaseUnitFactorFixed", "The base unit factor is always 1.");
}
