using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Admin;

/// <summary>Contract §11.1, AD1–AD3: the seeded admin, admin sessions, and which token opens which endpoint.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AdminAuthEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Seed_ManyHostStarts_CreateTheConfiguredAdminOnce()
    {
        await using (var first = fixture.CreateFactory())
        {
            using var client = first.CreateClient();
        }

        await using var second = fixture.CreateFactory();
        using var again = second.CreateClient();

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM users WHERE platform_role = 1")).Should().Be(1);
        (await fixture.ScalarAsync<string>("SELECT full_name FROM users WHERE platform_role = 1")).Should().Be(AdminApi.FullName);
    }

    [Fact]
    public async Task Login_Admin_200WithAnAdminTokenWithoutShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AdminApi.Login, new { email = AdminApi.Email, password = AdminApi.Password });

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["user"]!["email"]!.GetValue<string>().Should().Be(AdminApi.Email);
        response.Body["user"]!["fullName"]!.GetValue<string>().Should().Be(AdminApi.FullName);
        response.Body.AsObject().Select(pair => pair.Key).Should().BeEquivalentTo(
            "accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "user");
        var token = new JsonWebToken(response.Body["accessToken"]!.GetValue<string>());
        token.GetClaim("platform_role").Value.Should().Be("PlatformAdmin");
        token.TryGetClaim("shop_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Login_ShopUserOrWrongPassword_401InvalidCredentials()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);

        var shopUser = await AuthApi.PostAsync(client, AdminApi.Login, new { email = shop.Email, password = AuthApi.Password });
        var wrong = await AuthApi.PostAsync(client, AdminApi.Login, new { email = AdminApi.Email, password = "Wr0ngPassword" });
        var blank = await AuthApi.PostAsync(client, AdminApi.Login, new { email = "", password = "" });

        shopUser.Status.Should().Be(HttpStatusCode.Unauthorized);
        shopUser.Code.Should().Be("Auth.InvalidCredentials", "the endpoint does not reveal who is an admin");
        wrong.Code.Should().Be("Auth.InvalidCredentials");
        blank.Status.Should().Be(HttpStatusCode.BadRequest);
        blank.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task Refresh_AdminToken_RotatesAndTheOldOneIsThenReused()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);

        var rotated = await AuthApi.PostAsync(client, AdminApi.Refresh, new { refreshToken = admin.RefreshToken });
        var reused = await AuthApi.PostAsync(client, AdminApi.Refresh, new { refreshToken = admin.RefreshToken });

        rotated.Status.Should().Be(HttpStatusCode.OK, rotated.Raw);
        rotated.Body!["refreshToken"]!.GetValue<string>().Should().NotBe(admin.RefreshToken);
        reused.Status.Should().Be(HttpStatusCode.Unauthorized);
        reused.Code.Should().Be("Auth.RefreshTokenReused");
    }

    [Fact]
    public async Task Refresh_TokensAreNotInterchangeableBetweenShopAndAdmin()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);

        var shopTokenOnAdmin = await AuthApi.PostAsync(client, AdminApi.Refresh, new { refreshToken = shop.RefreshToken });
        var adminTokenOnShop = await AuthApi.RefreshAsync(client, admin.RefreshToken);

        shopTokenOnAdmin.Status.Should().Be(HttpStatusCode.Unauthorized);
        shopTokenOnAdmin.Code.Should().Be("Auth.InvalidRefreshToken");
        adminTokenOnShop.Status.Should().Be(HttpStatusCode.Unauthorized);
        adminTokenOnShop.Code.Should().Be("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Logout_Admin_204IdempotentAndTheFamilyIsRevoked()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);

        var first = await AuthApi.PostAsync(client, AdminApi.Logout, new { refreshToken = admin.RefreshToken }, bearer: admin.AccessToken);
        var again = await AuthApi.PostAsync(client, AdminApi.Logout, new { refreshToken = admin.RefreshToken }, bearer: admin.AccessToken);
        var refresh = await AuthApi.PostAsync(client, AdminApi.Refresh, new { refreshToken = admin.RefreshToken });

        first.Status.Should().Be(HttpStatusCode.NoContent);
        again.Status.Should().Be(HttpStatusCode.NoContent);
        refresh.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminLogout_WithAShopToken_403ForbiddenAndNothingRevoked()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);

        var response = await AuthApi.PostAsync(client, AdminApi.Logout, new { refreshToken = shop.RefreshToken }, bearer: shop.AccessToken);
        var refresh = await AuthApi.RefreshAsync(client, shop.RefreshToken);

        response.Status.Should().Be(HttpStatusCode.Forbidden, response.Raw);
        response.Code.Should().Be("Auth.Forbidden");
        refresh.Status.Should().Be(HttpStatusCode.OK, "the shop session was not touched");
    }

    [Fact]
    public async Task ShopLogout_WithAnAdminToken_403ForbiddenAndTheAdminSessionStays()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);

        var response = await AuthApi.LogoutAsync(client, admin.AccessToken, admin.RefreshToken);
        var refresh = await AuthApi.PostAsync(client, AdminApi.Refresh, new { refreshToken = admin.RefreshToken });

        response.Status.Should().Be(HttpStatusCode.Forbidden, response.Raw);
        response.Code.Should().Be("Auth.Forbidden");
        refresh.Status.Should().Be(HttpStatusCode.OK, "A4 rejected the admin token before revoking anything");
    }

    [Fact]
    public async Task AdminToken_OnShopEndpoints_403Forbidden()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);

        var products = await ProductApi.GetAsync(client, ProductApi.Products, admin.AccessToken);
        var me = await AuthApi.MeAsync(client, admin.AccessToken);
        var shopLogout = await AuthApi.LogoutAsync(client, admin.AccessToken, admin.RefreshToken);

        foreach (var response in new[] { products, me, shopLogout })
        {
            response.Status.Should().Be(HttpStatusCode.Forbidden, response.Raw);
            response.Code.Should().Be("Auth.Forbidden");
        }
    }

    [Theory]
    [InlineData("GET", "/api/admin/shops")]
    [InlineData("GET", "/api/admin/plans")]
    [InlineData("GET", "/api/admin/ai-usage/daily")]
    [InlineData("GET", "/api/admin/invoices/failed")]
    [InlineData("GET", "/api/admin/audit-logs")]
    [InlineData("POST", "/api/admin/hangfire/session")]
    public async Task ShopTokenOrNoToken_OnAdminEndpoints_403Or401(string method, string path)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);

        var withShopToken = await Send(client, method, path, shop.AccessToken);
        var anonymous = await Send(client, method, path, null);

        withShopToken.Status.Should().Be(HttpStatusCode.Forbidden, withShopToken.Raw);
        withShopToken.Code.Should().Be("Auth.Forbidden");
        anonymous.Status.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Code.Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task OwnerToken_OnAdminWrite_403Forbidden()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);

        var response = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "x" }, shop.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Auth.Forbidden");
    }

    private static Task<ApiResponse> Send(HttpClient client, string method, string path, string? token) =>
        method == "GET" ? AdminApi.GetAsync(client, path, token) : AdminApi.PostAsync(client, path, new { }, token);
}
