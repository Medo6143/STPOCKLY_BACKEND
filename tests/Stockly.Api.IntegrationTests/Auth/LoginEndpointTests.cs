using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>A2 <c>POST /api/auth/login</c>.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class LoginEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Login_ValidCredentialsAnyEmailCase_Returns200WithSession()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        var response = await AuthApi.PostAsync(
            client, AuthApi.Login, new { email = registered.Email.ToUpperInvariant(), password = AuthApi.Password, deviceId = "tab-1" });

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["shop"]!["id"]!.GetValue<Guid>().Should().Be(registered.ShopId);
        response.Body!["shop"]!["role"]!.GetValue<string>().Should().Be("Owner");
        response.Body!["refreshToken"]!.GetValue<string>().Should().NotBe(registered.RefreshToken);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM users WHERE id = @p0 AND last_login_at IS NOT NULL", registered.UserId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_SameCode401InvalidCredentials()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        var wrongPassword = await AuthApi.PostAsync(client, AuthApi.Login, new { email = registered.Email, password = "Wr0ngPass" });
        var unknownEmail = await AuthApi.PostAsync(client, AuthApi.Login, new { email = AuthApi.NewEmail(), password = AuthApi.Password });

        wrongPassword.Status.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.Code.Should().Be("Auth.InvalidCredentials");
        unknownEmail.Status.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.Code.Should().Be("Auth.InvalidCredentials");
        unknownEmail.Body!["detail"]!.GetValue<string>().Should().Be(wrongPassword.Body!["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Login_MissingFields_Returns400ValidationFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Login, new { email = "" });

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!.AsObject().Select(pair => pair.Key).Should().BeEquivalentTo("email", "password");
    }

    [Fact]
    public async Task Login_SuspendedShop_Returns403ShopSuspended()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", registered.ShopId);

        var response = await AuthApi.PostAsync(client, AuthApi.Login, new { email = registered.Email, password = AuthApi.Password });

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Shop.Suspended");
    }

    [Fact]
    public async Task Login_NoActiveMembership_Returns403NoActiveShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shop_members SET is_active = false WHERE user_id = @p0", registered.UserId);

        var response = await AuthApi.PostAsync(client, AuthApi.Login, new { email = registered.Email, password = AuthApi.Password });

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Auth.NoActiveShop");
    }

    [Fact]
    public async Task Login_SeveralMemberships_TokenIsForTheActiveMembershipJoinedFirst()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var user = await AuthApi.RegisterAsync(client);
        var otherOwner = await AuthApi.RegisterAsync(client);
        var inactiveOwner = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync(
            "INSERT INTO shop_members (shop_id, user_id, role, is_active, joined_at) VALUES (@p0, @p1, 2, true, '2000-01-02T00:00:00Z')",
            otherOwner.ShopId,
            user.UserId);
        await fixture.ExecuteAsync(
            "INSERT INTO shop_members (shop_id, user_id, role, is_active, joined_at) VALUES (@p0, @p1, 2, false, '2000-01-01T00:00:00Z')",
            inactiveOwner.ShopId,
            user.UserId);

        var session = await AuthApi.LoginAsync(client, user.Email);

        session.ShopId.Should().Be(otherOwner.ShopId, "the active membership joined first wins; inactive ones are ignored");
        session.Body["shop"]!["role"]!.GetValue<string>().Should().Be("Staff");
    }
}
