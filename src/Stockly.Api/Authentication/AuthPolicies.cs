namespace Stockly.Api.Authentication;

/// <summary>
/// Authorization policies. <c>[Authorize]</c> (the default policy) = authenticated and the token's shop not suspended.
/// <see cref="Owner"/> adds role Owner (used from step 1.6). <see cref="AllowSuspendedShop"/> is authentication only,
/// for logout, so a suspended shop can always sign out (contract §1.4); it still needs a shop token. <see cref="PlatformAdmin"/>
/// is an admin token: claim <c>platform_role=PlatformAdmin</c> and no <c>shop_id</c> (contract §11.1, ADR-0005). A shop
/// token on an admin endpoint, or an admin token on a shop endpoint, is <c>403 Auth.Forbidden</c>.
/// </summary>
public static class AuthPolicies
{
    public const string Owner = "Owner";
    public const string AllowSuspendedShop = "AllowSuspendedShop";
    public const string PlatformAdmin = "PlatformAdmin";
}
