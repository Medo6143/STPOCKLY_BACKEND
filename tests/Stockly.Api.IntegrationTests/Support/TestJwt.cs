using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stockly.Application.Abstractions.Identity;
using Stockly.Infrastructure.Identity;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>The test signing key and a minter for tokens the Api would never issue (expired, other key, Staff role...).</summary>
internal static class TestJwt
{
    /// <summary>Test-only value, 48 bytes. Never a real secret.</summary>
    public const string SigningKey = "test-only-signing-key-0123456789-abcdefghijklmno";

    public const string OtherSigningKey = "another-test-only-key-0123456789-abcdefghijklmno";

    public static string Mint(
        Guid userId,
        Guid shopId,
        string role,
        DateTimeOffset? expiresAt = null,
        string signingKey = SigningKey,
        string issuer = JwtOptions.DefaultIssuer)
    {
        var expires = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15);
        var options = new JwtOptions { SigningKey = signingKey };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = JwtOptions.DefaultAudience,
            IssuedAt = expires.AddMinutes(-15).UtcDateTime,
            NotBefore = expires.AddMinutes(-15).UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [StocklyClaimTypes.Subject] = userId.ToString(),
                [StocklyClaimTypes.ShopId] = shopId.ToString(),
                [StocklyClaimTypes.Role] = role,
                [StocklyClaimTypes.TokenId] = Guid.NewGuid().ToString(),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
