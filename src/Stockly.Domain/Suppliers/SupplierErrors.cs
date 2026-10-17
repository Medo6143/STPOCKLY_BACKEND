using Stockly.Domain.Common;

namespace Stockly.Domain.Suppliers;

public static class SupplierErrors
{
    public static readonly Error ShopRequired =
        Error.Validation("Supplier.ShopRequired", "A supplier must belong to a shop.");

    public static readonly Error NameRequired =
        Error.Validation("Supplier.NameRequired", "Supplier name is required.");

    public static readonly Error AliasTextRequired =
        Error.Validation("ProductAlias.TextRequired", "An alias needs the text written on the invoice.");

    public static readonly Error TimesConfirmedNotPositive =
        Error.Validation("ProductAlias.TimesConfirmedNotPositive", "An alias is confirmed at least once.");
}
