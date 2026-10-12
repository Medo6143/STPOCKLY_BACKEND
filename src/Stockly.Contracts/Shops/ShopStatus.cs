namespace Stockly.Contracts.Shops;

/// <summary>Serialized as the PascalCase name, never a number (contract §1.1).</summary>
public enum ShopStatus
{
    Active = 1,
    Suspended = 2,
}
