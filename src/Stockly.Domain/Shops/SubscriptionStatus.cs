namespace Stockly.Domain.Shops;

/// <summary>State of a shop's subscription. Stored as smallint: values are never reordered, only appended.</summary>
public enum SubscriptionStatus
{
    Active = 1,
    Ended = 2,
}
