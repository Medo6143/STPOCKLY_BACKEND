namespace Stockly.Api.Authentication;

/// <summary>
/// Authorization policies. <c>[Authorize]</c> (the default policy) = authenticated and the token's shop not suspended.
/// <see cref="Owner"/> adds role Owner (used from step 1.6). <see cref="AllowSuspendedShop"/> is authentication only,
/// for logout, so a suspended shop can always sign out (contract §1.4).
/// </summary>
public static class AuthPolicies
{
    public const string Owner = "Owner";
    public const string AllowSuspendedShop = "AllowSuspendedShop";
}
