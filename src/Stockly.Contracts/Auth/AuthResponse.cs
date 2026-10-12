namespace Stockly.Contracts.Auth;

/// <summary>
/// Returned by register, login and refresh (contract §3). The refresh token belongs in secure storage only.
/// Expiry times are UTC (<see cref="DateTimeKind.Utc"/>), serialized ISO-8601 with <c>Z</c>.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User,
    ShopMembershipDto Shop);
