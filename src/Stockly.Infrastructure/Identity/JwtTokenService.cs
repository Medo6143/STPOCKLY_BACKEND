using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// HMAC-SHA256 access tokens (15 minutes) and opaque refresh tokens: 32 random bytes, base64url, stored only as their
/// lower-case hex SHA-256. Times come from <see cref="IClock"/>, truncated to whole seconds so the reported expiry
/// equals the JWT <c>exp</c>.
/// </summary>
public sealed class JwtTokenService(JwtOptions options, IClock clock) : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(options.CreateSigningKey(), SecurityAlgorithms.HmacSha256);

    public AccessToken CreateAccessToken(Guid userId, Guid shopId, ShopRole role) =>
        Create(new Dictionary<string, object>
        {
            [StocklyClaimTypes.Subject] = userId.ToString(),
            [StocklyClaimTypes.ShopId] = shopId.ToString(),
            [StocklyClaimTypes.Role] = role.ToString(),
            [StocklyClaimTypes.TokenId] = Guid.NewGuid().ToString(),
        });

    public AccessToken CreateAdminAccessToken(Guid userId) =>
        Create(new Dictionary<string, object>
        {
            [StocklyClaimTypes.Subject] = userId.ToString(),
            [StocklyClaimTypes.PlatformRole] = StocklyClaimTypes.PlatformAdminValue,
            [StocklyClaimTypes.TokenId] = Guid.NewGuid().ToString(),
        });

    public RefreshTokenValue CreateRefreshToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new RefreshTokenValue(token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private AccessToken Create(Dictionary<string, object> claims)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.UtcNow.ToUnixTimeSeconds());
        var expiresAt = now + TokenLifetimes.AccessToken;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = claims,
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
