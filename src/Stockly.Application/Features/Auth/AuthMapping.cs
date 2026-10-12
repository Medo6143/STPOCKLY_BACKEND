using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Auth;
using Stockly.Domain.Users;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;
using ContractShopStatus = Stockly.Contracts.Shops.ShopStatus;
using ContractShopType = Stockly.Contracts.Shops.ShopType;
using ShopType = Stockly.Domain.Shops.ShopType;

namespace Stockly.Application.Features.Auth;

/// <summary>Maps auth data to contract DTOs and parses the contract's string values.</summary>
public static class AuthMapping
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static UserDto ToDto(this UserAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(user.Id, user.Email, user.FullName, user.Phone, ToCode(user.PreferredLanguage));
    }

    /// <summary>Contract enums share the Domain enums' names and values (guarded by a unit test).</summary>
    public static ShopMembershipDto ToDto(this ShopMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        return new ShopMembershipDto(
            membership.ShopId,
            membership.ShopName,
            (ContractShopType)membership.ShopType,
            (ContractShopStatus)membership.ShopStatus,
            (ContractShopRole)membership.Role);
    }

    public static string ToCode(PreferredLanguage language) => language == PreferredLanguage.En ? English : Arabic;

    /// <summary><c>null</c> means the default, Arabic. Unsupported codes are rejected by the validators first.</summary>
    public static PreferredLanguage ToPreferredLanguage(string? code) =>
        code == English ? PreferredLanguage.En : PreferredLanguage.Ar;

    public static bool IsSupportedLanguage(string? code) => code is null or Arabic or English;

    /// <summary>Exact PascalCase name of a shop type (contract §1.1); numbers and other casings are not accepted.</summary>
    public static bool TryParseShopType(string? value, out ShopType shopType)
    {
        shopType = default;
        return value is not null
            && Enum.GetNames<ShopType>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out shopType);
    }
}
