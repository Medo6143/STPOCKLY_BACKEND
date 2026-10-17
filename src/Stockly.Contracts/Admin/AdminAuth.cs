namespace Stockly.Contracts.Admin;

/// <summary>AD1 <c>POST /api/admin/auth/login</c>.</summary>
public sealed record AdminLoginRequest(string Email, string Password);

/// <summary>
/// Returned by AD1 and AD2 (contract §11.2). The access token carries <c>platform_role=PlatformAdmin</c> and no shop.
/// Expiry times are UTC, serialized ISO-8601 with <c>Z</c>.
/// </summary>
public sealed record AdminAuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    AdminUserDto User);

/// <summary>The signed-in platform admin.</summary>
public sealed record AdminUserDto(Guid Id, string Email, string FullName);
