using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Identity;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Identity;

public sealed class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new() { SigningKey = PostgresFixture.TestSigningKey };
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 7, 0, 0, 750, TimeSpan.Zero);

    [Fact]
    public async Task CreateAccessToken_ValidatesWithTheOptionsAndCarriesTheContractClaims()
    {
        var service = new JwtTokenService(Options, new FakeClock(DateTimeOffset.UtcNow));
        var userId = Guid.CreateVersion7();
        var shopId = Guid.CreateVersion7();

        var access = service.CreateAccessToken(userId, shopId, ShopRole.Staff);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(access.Token, Options.CreateValidationParameters());

        validation.IsValid.Should().BeTrue();
        var token = (JsonWebToken)validation.SecurityToken;
        token.GetClaim("sub").Value.Should().Be(userId.ToString());
        token.GetClaim("shop_id").Value.Should().Be(shopId.ToString());
        token.GetClaim("role").Value.Should().Be("Staff");
        Guid.TryParse(token.GetClaim("jti").Value, out _).Should().BeTrue();
        token.Issuer.Should().Be("stockly");
        token.Audiences.Should().Equal("stockly-app");
    }

    [Fact]
    public async Task CreateAdminAccessToken_HasThePlatformRoleAndNoShop()
    {
        var service = new JwtTokenService(Options, new FakeClock(DateTimeOffset.UtcNow));
        var userId = Guid.CreateVersion7();

        var access = service.CreateAdminAccessToken(userId);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(access.Token, Options.CreateValidationParameters());

        validation.IsValid.Should().BeTrue();
        var token = (JsonWebToken)validation.SecurityToken;
        token.GetClaim("sub").Value.Should().Be(userId.ToString());
        token.GetClaim("platform_role").Value.Should().Be("PlatformAdmin");
        token.TryGetClaim("shop_id", out _).Should().BeFalse();
        token.TryGetClaim("role", out _).Should().BeFalse();
        access.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateAccessToken_ExpiresIn15MinutesFromTheClock_InWholeSeconds()
    {
        var service = new JwtTokenService(Options, new FakeClock(Now));

        var access = service.CreateAccessToken(Guid.CreateVersion7(), Guid.CreateVersion7(), ShopRole.Owner);
        var token = new JsonWebToken(access.Token);

        access.ExpiresAt.Should().Be(new DateTimeOffset(2026, 10, 12, 7, 15, 0, TimeSpan.Zero));
        token.ValidTo.Should().Be(access.ExpiresAt.UtcDateTime);
        token.IssuedAt.Should().Be(new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CreateAccessToken_OtherKey_DoesNotValidate()
    {
        var service = new JwtTokenService(Options, new FakeClock(DateTimeOffset.UtcNow));
        var other = new JwtOptions { SigningKey = "another-test-only-key-0123456789-abcdefghijklmno" };

        var access = service.CreateAccessToken(Guid.CreateVersion7(), Guid.CreateVersion7(), ShopRole.Owner);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(access.Token, other.CreateValidationParameters());

        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateRefreshToken_Is32RandomBytesBase64Url_WithItsSha256Hash()
    {
        var service = new JwtTokenService(Options, new FakeClock(Now));

        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        first.Token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        first.Token.Should().NotBe(second.Token);
        first.Hash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(first.Token))));
        service.HashRefreshToken(first.Token).Should().Be(first.Hash);
    }

    [Fact]
    public void JwtOptions_ToString_NeverShowsTheKey()
    {
        Options.ToString().Should().NotContain(PostgresFixture.TestSigningKey);
    }
}
