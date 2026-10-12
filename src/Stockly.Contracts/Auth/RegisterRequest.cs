namespace Stockly.Contracts.Auth;

/// <summary>A1 <c>POST /api/auth/register</c>. Optional fields have defaults so the spec does not list them as required.</summary>
public sealed record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    RegisterShopRequest Shop,
    string? Phone = null,
    string? PreferredLanguage = null,
    string? DeviceId = null);

/// <summary>The shop created with the user. <see cref="ShopType"/>: <c>Pharmacy</c>, <c>Grocery</c>, <c>MobileShop</c> or <c>Other</c>.</summary>
public sealed record RegisterShopRequest(
    string Name,
    string ShopType,
    string? Phone = null,
    string? Governorate = null,
    string? Address = null);
