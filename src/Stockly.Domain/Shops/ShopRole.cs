namespace Stockly.Domain.Shops;

/// <summary>A member's role in a shop. Stored as smallint: values are never reordered, only appended.</summary>
public enum ShopRole
{
    Owner = 1,
    Staff = 2,
}
