using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class ShopErrors
{
    public static readonly Error NameRequired =
        Error.Validation("Shop.NameRequired", "Shop name is required.");

    public static readonly Error UnknownType =
        Error.Validation("Shop.UnknownType", "The shop type is not supported.");

    public static readonly Error Suspended =
        Error.Forbidden("Shop.Suspended", "The shop is suspended by the platform admin.");
}
