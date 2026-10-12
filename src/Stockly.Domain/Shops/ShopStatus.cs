namespace Stockly.Domain.Shops;

/// <summary>Set by the platform admin. Stored as smallint: values are never reordered, only appended.</summary>
public enum ShopStatus
{
    Active = 1,
    Suspended = 2,
}
