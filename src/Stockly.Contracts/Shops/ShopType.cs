namespace Stockly.Contracts.Shops;

/// <summary>Serialized as the PascalCase name, never a number (contract §1.1).</summary>
public enum ShopType
{
    Pharmacy = 1,
    Grocery = 2,
    MobileShop = 3,
    Other = 4,
}
