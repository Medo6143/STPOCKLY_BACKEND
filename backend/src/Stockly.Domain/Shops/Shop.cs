using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Shops;

/// <summary>A shop: the tenant (ADR-0002). Not an <see cref="ITenantEntity"/> itself; tenant rows point to it.</summary>
public sealed class Shop : Entity
{
    private Shop(string name, ShopType shopType, string? phone, string? governorate, string? address, ShopStatus status, DateTimeOffset createdAt)
    {
        Name = name;
        ShopType = shopType;
        Phone = phone;
        Governorate = governorate;
        Address = address;
        Status = status;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    public ShopType ShopType { get; private set; }

    public string? Phone { get; private set; }

    public string? Governorate { get; private set; }

    public string? Address { get; private set; }

    public ShopStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
