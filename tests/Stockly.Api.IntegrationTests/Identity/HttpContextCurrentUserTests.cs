using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stockly.Api.Identity;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Shops;

namespace Stockly.Api.IntegrationTests.Identity;

public sealed class HttpContextCurrentUserTests
{
    [Fact]
    public void UserId_ValidSubClaim_ReturnsUser()
    {
        var userId = Guid.CreateVersion7();

        User(new Claim(StocklyClaimTypes.Subject, userId.ToString())).UserId.Should().Be(userId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void UserId_MalformedOrEmptyClaim_ReturnsNull(string value)
    {
        User(new Claim(StocklyClaimTypes.Subject, value)).UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_NoClaimOrNoContext_ReturnsNull()
    {
        User().UserId.Should().BeNull();
        new HttpContextCurrentUser(new HttpContextAccessor()).UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_TwoDifferentClaims_ReturnsNull()
    {
        User(
            new Claim(StocklyClaimTypes.Subject, Guid.CreateVersion7().ToString()),
            new Claim(StocklyClaimTypes.Subject, Guid.CreateVersion7().ToString()))
            .UserId.Should().BeNull();
    }

    [Theory]
    [InlineData("Owner", ShopRole.Owner)]
    [InlineData("Staff", ShopRole.Staff)]
    public void Role_ExactRoleName_ReturnsRole(string value, ShopRole role)
    {
        User(new Claim(StocklyClaimTypes.Role, value)).Role.Should().Be(role);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("1")]
    [InlineData("Admin")]
    [InlineData("")]
    public void Role_OtherValue_ReturnsNull(string value)
    {
        User(new Claim(StocklyClaimTypes.Role, value)).Role.Should().BeNull();
    }

    [Fact]
    public void Role_MissingOrAmbiguous_ReturnsNull()
    {
        User().Role.Should().BeNull();
        new HttpContextCurrentUser(new HttpContextAccessor()).Role.Should().BeNull();
        User(new Claim(StocklyClaimTypes.Role, "Owner"), new Claim(StocklyClaimTypes.Role, "Staff")).Role.Should().BeNull();
    }

    private static HttpContextCurrentUser User(params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}
