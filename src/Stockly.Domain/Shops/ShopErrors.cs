using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class ShopErrors
{
    public static readonly Error NameRequired =
        Error.Validation("Shop.NameRequired", "Shop name is required.");

    public static readonly Error UnknownType =
        Error.Validation("Shop.UnknownType", "The shop type is not supported.");

    /// <summary>No such shop (admin endpoints AD5–AD10).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Shop.NotFound", "The shop was not found.");

    public static readonly Error Suspended =
        Error.Forbidden("Shop.Suspended", "The shop is suspended by the platform admin.");
}
