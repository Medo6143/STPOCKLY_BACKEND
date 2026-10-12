namespace Stockly.Domain.Shops;

/// <summary>Kind of shop. Stored as smallint: values are never reordered, only appended.</summary>
public enum ShopType
{
    Pharmacy = 1,
    Grocery = 2,
    MobileShop = 3,
    Other = 4,
}
