using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Shops;

/// <summary>A shop: the tenant (ADR-0002). Not an <see cref="ITenantEntity"/> itself; tenant rows point to it.</summary>
public sealed class Shop : Entity
{
    private Shop(string name, ShopType shopType, string? phone, string? governorate, string? address, ShopStatus status, DateTimeOffset createdAt)
    {
        Name = name;
        NormalizedName = ArabicTextNormalizer.Normalize(name);
        ShopType = shopType;
        Phone = phone;
        Governorate = governorate;
        Address = address;
        Status = status;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    /// <summary>The admin search key (<see cref="ArabicTextNormalizer"/>), kept in step with <see cref="Name"/>.</summary>
    public string NormalizedName { get; private set; }

    public ShopType ShopType { get; private set; }

    public string? Phone { get; private set; }

    public string? Governorate { get; private set; }

    public string? Address { get; private set; }

    public ShopStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the platform admin suspended the shop; <c>null</c> while active.</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>The admin's reason for the suspension; <c>null</c> while active.</summary>
    public string? SuspendedReason { get; private set; }

    public static Result<Shop> Create(
        string name, ShopType shopType, string? phone, string? governorate, string? address, DateTimeOffset now)
    {
        if (ArabicTextNormalizer.Normalize(name).Length == 0)
        {
            return ShopErrors.NameRequired;
        }

        if (!Enum.IsDefined(shopType))
        {
            return ShopErrors.UnknownType;
        }

        return new Shop(name.Trim(), shopType, Clean(phone), Clean(governorate), Clean(address), ShopStatus.Active, now);
    }

    /// <summary>
    /// Suspends the shop (contract AD6). Idempotent: an already suspended shop keeps its first suspension and the method
    /// returns <c>false</c> (nothing changed, nothing to audit).
    /// </summary>
    public bool Suspend(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status == ShopStatus.Suspended)
        {
            return false;
        }

        Status = ShopStatus.Suspended;
        SuspendedAt = now;
        SuspendedReason = reason.Trim();
        return true;
    }

    /// <summary>Reactivates the shop (contract AD7). Idempotent: returns <c>false</c> when it was already active.</summary>
    public bool Activate()
    {
        if (Status == ShopStatus.Active)
        {
            return false;
        }

        Status = ShopStatus.Active;
        SuspendedAt = null;
        SuspendedReason = null;
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
