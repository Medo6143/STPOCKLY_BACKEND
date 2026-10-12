using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>A3 <c>POST /api/auth/refresh</c> and A4 <c>POST /api/auth/logout</c>: rotation, families, reuse detection.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class RefreshAndLogoutEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task FullSession_RegisterMeRefreshReuseLogout_BehavesPerContract()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var registered = await AuthApi.RegisterAsync(client);
        (await AuthApi.MeAsync(client, registered.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

        var refreshed = await AuthApi.RefreshAsync(client, registered.RefreshToken);
        refreshed.Status.Should().Be(HttpStatusCode.OK, refreshed.Raw);
        var rotated = Session.From(registered.Email, refreshed.Body!);
        rotated.RefreshToken.Should().NotBe(registered.RefreshToken);
        rotated.ShopId.Should().Be(registered.ShopId);
        (await AuthApi.MeAsync(client, rotated.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

        var reuse = await AuthApi.RefreshAsync(client, registered.RefreshToken);
        reuse.Status.Should().Be(HttpStatusCode.Unauthorized);
        reuse.Code.Should().Be("Auth.RefreshTokenReused");

        var rotatedAfterReuse = await AuthApi.RefreshAsync(client, rotated.RefreshToken);
        rotatedAfterReuse.Status.Should().Be(HttpStatusCode.Unauthorized, "reuse revokes the whole family, including its active token");

        (await AuthApi.LogoutAsync(client, rotated.AccessToken, rotated.RefreshToken)).Status.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reuse_OnDeviceA_RevokesOnlyDeviceAFamily_DeviceBStillRefreshes()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        var deviceA = await AuthApi.LoginAsync(client, registered.Email, "device-a");
        var deviceB = await AuthApi.LoginAsync(client, registered.Email, "device-b");

        var a2 = await AuthApi.RefreshAsync(client, deviceA.RefreshToken);
        a2.Status.Should().Be(HttpStatusCode.OK);

        var a1Again = await AuthApi.RefreshAsync(client, deviceA.RefreshToken);
        a1Again.Status.Should().Be(HttpStatusCode.Unauthorized);
        a1Again.Code.Should().Be("Auth.RefreshTokenReused");

        var a2Refresh = await AuthApi.RefreshAsync(client, a2.Body!["refreshToken"]!.GetValue<string>());
        a2Refresh.Status.Should().Be(HttpStatusCode.Unauthorized, "A2 belongs to the revoked family");

        var bRefresh = await AuthApi.RefreshAsync(client, deviceB.RefreshToken);
        bRefresh.Status.Should().Be(HttpStatusCode.OK, "other devices' families are unaffected");
        (await AuthApi.RefreshAsync(client, registered.RefreshToken)).Status.Should().Be(HttpStatusCode.OK, "the register session is its own family");
    }

    [Fact]
    public async Task Refresh_RotationChain_IsPersistedWithReplacedByAndFamily()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        (await AuthApi.RefreshAsync(client, registered.RefreshToken)).Status.Should().Be(HttpStatusCode.OK);

        (await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM refresh_tokens o JOIN refresh_tokens n ON n.id = o.replaced_by_id " +
            "WHERE o.user_id = @p0 AND o.revoked_at IS NOT NULL AND n.revoked_at IS NULL AND n.family_id = o.family_id AND o.family_id = o.id",
            registered.UserId))
            .Should().Be(1);
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_UnknownToken_Returns401InvalidRefreshToken(string token)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.RefreshAsync(client, token);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401InvalidRefreshToken()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE refresh_tokens SET expires_at = now() - interval '1 second' WHERE user_id = @p0", registered.UserId);

        var response = await AuthApi.RefreshAsync(client, registered.RefreshToken);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Refresh_MissingToken_Returns400ValidationFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Refresh, new { });

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!["refreshToken"].Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_SuspendedShop_Returns403ShopSuspended()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", registered.ShopId);

        var response = await AuthApi.RefreshAsync(client, registered.RefreshToken);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Shop.Suspended");
    }

    [Fact]
    public async Task Logout_RevokesTheWholeFamily_TokenNoLongerRefreshes()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        var rotated = Session.From(registered.Email, (await AuthApi.RefreshAsync(client, registered.RefreshToken)).Body!);

        var logout = await AuthApi.LogoutAsync(client, rotated.AccessToken, registered.RefreshToken);

        logout.Status.Should().Be(HttpStatusCode.NoContent);
        (await AuthApi.RefreshAsync(client, rotated.RefreshToken)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_UnknownOrAlreadyRevokedToken_StillReturns204()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        (await AuthApi.LogoutAsync(client, registered.AccessToken, "unknown-token")).Status.Should().Be(HttpStatusCode.NoContent);
        (await AuthApi.LogoutAsync(client, registered.AccessToken, registered.RefreshToken)).Status.Should().Be(HttpStatusCode.NoContent);
        (await AuthApi.LogoutAsync(client, registered.AccessToken, registered.RefreshToken)).Status.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_AnotherUsersToken_Returns204AndLeavesItActive()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var attacker = await AuthApi.RegisterAsync(client);
        var victim = await AuthApi.RegisterAsync(client);

        (await AuthApi.LogoutAsync(client, attacker.AccessToken, victim.RefreshToken)).Status.Should().Be(HttpStatusCode.NoContent);

        (await AuthApi.RefreshAsync(client, victim.RefreshToken)).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_SuspendedShop_StillReturns204()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", registered.ShopId);

        var response = await AuthApi.LogoutAsync(client, registered.AccessToken, registered.RefreshToken);

        response.Status.Should().Be(HttpStatusCode.NoContent, "a suspended shop can always sign out");
    }

    [Fact]
    public async Task Logout_WithoutAccessToken_Returns401()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Logout, new { refreshToken = "anything" });

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Logout_MissingRefreshToken_Returns400ValidationFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        var response = await AuthApi.PostAsync(client, AuthApi.Logout, new { }, bearer: registered.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
    }
}
