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
        new HttpContextCurrentTenant(new HttpContextAccessor(), new RequestTenantScope()).ShopId.Should().BeNull();
    }

    [Fact]
    public void ShopId_OpenTenantScope_WinsOverClaimUntilDisposed()
    {
        var claimShop = Guid.CreateVersion7();
        var newShop = Guid.CreateVersion7();
        var scope = new RequestTenantScope();
        var tenant = Tenant(scope, new Claim(StocklyClaimTypes.ShopId, claimShop.ToString()));

        using (scope.BeginForNewShop(newShop))
        {
            tenant.ShopId.Should().Be(newShop);
        }

        tenant.ShopId.Should().Be(claimShop);
    }

    [Fact]
    public void ShopId_OpenTenantScopeWithoutHttpContext_ReturnsScopeShop()
    {
        var scope = new RequestTenantScope();
        var shopId = Guid.CreateVersion7();
        using var open = scope.BeginForNewShop(shopId);

        new HttpContextCurrentTenant(new HttpContextAccessor(), scope).ShopId.Should().Be(shopId);
    }

    [Fact]
    public void BeginForNewShop_AlreadyOpen_Throws()
    {
        var scope = new RequestTenantScope();
        using var open = scope.BeginForNewShop(Guid.CreateVersion7());

        var act = () => scope.BeginForNewShop(Guid.CreateVersion7());

        act.Should().Throw<InvalidOperationException>("tenant scopes do not nest");
    }

    [Fact]
    public void BeginForNewShop_EmptyShop_Throws()
    {
        var act = () => new RequestTenantScope().BeginForNewShop(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    private static HttpContextCurrentTenant Tenant(params Claim[] claims) => Tenant(new RequestTenantScope(), claims);

    private static HttpContextCurrentTenant Tenant(RequestTenantScope scope, params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new HttpContextCurrentTenant(new HttpContextAccessor { HttpContext = context }, scope);
    }
}
