using Stockly.Domain.Common;

namespace Stockly.Domain.Products;

public static class ProductUnitErrors
{
    public static readonly Error NameRequired =
        Error.Validation("ProductUnit.NameRequired", "Unit name is required.");

    public static readonly Error DuplicateName =
        Error.Conflict("ProductUnit.DuplicateName", "This product already has a unit with this name.");

    public static readonly Error DuplicateBarcode =
        Error.Conflict("ProductUnit.DuplicateBarcode", "A unit of this shop already has this barcode.");

    public static readonly Error NotFound =
        Error.NotFound("ProductUnit.NotFound", "The unit was not found on this product.");

    public static readonly Error BaseUnitCannotBeRemoved =
        Error.Validation("ProductUnit.BaseUnitCannotBeRemoved", "The base unit cannot be removed.");

    /// <summary>The unit is referenced by invoice or sale lines; deleting it would break their history (contract P8).</summary>
    public static readonly Error InUse =
        Error.Conflict("ProductUnit.InUse", "This unit is used by invoice or sale lines and cannot be removed.");

    public static readonly Error BaseUnitFactorFixed =
        Error.Validation("ProductUnit.BaseUnitFactorFixed", "The base unit factor is always 1.");
}
