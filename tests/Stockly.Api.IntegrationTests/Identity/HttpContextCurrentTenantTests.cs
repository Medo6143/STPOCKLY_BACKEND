using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stockly.Api.Identity;
using Stockly.Application.Abstractions.Identity;

namespace Stockly.Api.IntegrationTests.Identity;

public sealed class HttpContextCurrentTenantTests
{
    [Fact]
    public void ShopId_ValidClaim_ReturnsShop()
    {
        var shopId = Guid.CreateVersion7();

        Tenant(new Claim(StocklyClaimTypes.ShopId, shopId.ToString())).ShopId.Should().Be(shopId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ShopId_MalformedOrEmptyClaim_ReturnsNull(string value)
    {
        Tenant(new Claim(StocklyClaimTypes.ShopId, value)).ShopId.Should().BeNull("a bad claim must fail closed");
    }

    [Fact]
    public void ShopId_NoClaim_ReturnsNull()
    {
        Tenant().ShopId.Should().BeNull();
    }

    [Fact]
    public void ShopId_TwoDifferentClaims_ReturnsNull()
    {
        Tenant(
            new Claim(StocklyClaimTypes.ShopId, Guid.CreateVersion7().ToString()),
            new Claim(StocklyClaimTypes.ShopId, Guid.CreateVersion7().ToString()))
            .ShopId.Should().BeNull("an ambiguous tenant must fail closed");
    }

    [Fact]
    public void ShopId_NoHttpContext_ReturnsNull()
    {
        new HttpContextCurrentTenant(new HttpContextAccessor()).ShopId.Should().BeNull();
    }

    private static HttpContextCurrentTenant Tenant(params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new HttpContextCurrentTenant(new HttpContextAccessor { HttpContext = context });
    }
}
