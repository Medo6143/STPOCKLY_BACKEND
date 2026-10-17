using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Identity;

/// <summary>Issues access tokens and refresh token values. Never logs a token.</summary>
public interface ITokenService
{
    /// <summary>A signed JWT with <c>sub</c>, <c>shop_id</c>, <c>role</c> and <c>jti</c>, valid for <see cref="TokenLifetimes.AccessToken"/>.</summary>
    AccessToken CreateAccessToken(Guid userId, Guid shopId, ShopRole role);

    /// <summary>
    /// A platform admin's JWT (contract §11.1): <c>sub</c>, <c>platform_role=PlatformAdmin</c> and <c>jti</c>, no
    /// <c>shop_id</c>, valid for <see cref="TokenLifetimes.AccessToken"/>.
    /// </summary>
    AccessToken CreateAdminAccessToken(Guid userId);

    /// <summary>A new random refresh token value and the hash to store (only the hash is ever stored).</summary>
    RefreshTokenValue CreateRefreshToken();

    /// <summary>The stored form of a presented refresh token.</summary>
    string HashRefreshToken(string refreshToken);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenValue(string Token, string Hash);
