using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>A5 <c>GET /api/auth/me</c>, access token validation and the per-request suspended-shop check.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class MeEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Me_ValidToken_ReturnsUserAndActiveShopWithRole()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        var response = await AuthApi.MeAsync(client, registered.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["user"]!.ToJsonString().Should().Be(registered.Body["user"]!.ToJsonString());
        response.Body!["shop"]!.ToJsonString().Should().Be(registered.Body["shop"]!.ToJsonString());
        response.Body!["user"]!["preferredLanguage"]!.GetValue<string>().Should().Be("ar", "Arabic is the default");
    }

    [Fact]
    public async Task Me_NoToken_Returns401UnauthenticatedWithBearerChallenge()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.MeAsync(client, accessToken: null);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
        response.ContentType.Should().Be("application/problem+json");
        response.WwwAuthenticate.Should().Be("Bearer");
        response.Body!["traceId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.c2lnbmF0dXJl")]
    public async Task Me_GarbageToken_Returns401Unauthenticated(string token)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.MeAsync(client, token);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
        response.Raw.Should().NotContain(token);
    }

    [Fact]
    public async Task Me_ExpiredToken_Returns401Unauthenticated()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        var expired = TestJwt.Mint(registered.UserId, registered.ShopId, "Owner", DateTimeOffset.UtcNow.AddSeconds(-5));

        var response = await AuthApi.MeAsync(client, expired);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Me_TokenSignedWithAnotherKeyOrIssuer_Returns401Unauthenticated()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        var otherKey = await AuthApi.MeAsync(client, TestJwt.Mint(registered.UserId, registered.ShopId, "Owner", signingKey: TestJwt.OtherSigningKey));
        var otherIssuer = await AuthApi.MeAsync(client, TestJwt.Mint(registered.UserId, registered.ShopId, "Owner", issuer: "someone-else"));

        otherKey.Status.Should().Be(HttpStatusCode.Unauthorized);
        otherIssuer.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_TokenForAShopTheUserIsNotMemberOf_Returns401Unauthenticated()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var user = await AuthApi.RegisterAsync(client);
        var otherShop = await AuthApi.RegisterAsync(client);
        var wrongTenant = TestJwt.Mint(user.UserId, otherShop.ShopId, "Owner");

        var response = await AuthApi.MeAsync(client, wrongTenant);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
        response.Raw.Should().NotContain(otherShop.Email);
    }

    [Fact]
    public async Task Me_SuspendedShop_Returns403ShopSuspended()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", registered.ShopId);

        var response = await AuthApi.MeAsync(client, registered.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Shop.Suspended");
    }

    [Fact]
    public async Task OwnerPolicy_Owner_Allowed_Staff_Returns403AuthForbidden()
    {
        await using var factory = fixture.CreateFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        var staffToken = TestJwt.Mint(registered.UserId, registered.ShopId, "Staff");

        var asOwner = await AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri("/__tests/owner-only", UriKind.Relative)), bearer: registered.AccessToken);
        var asStaff = await AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri("/__tests/owner-only", UriKind.Relative)), bearer: staffToken);

        asOwner.Status.Should().Be(HttpStatusCode.NoContent);
        asStaff.Status.Should().Be(HttpStatusCode.Forbidden);
        asStaff.Code.Should().Be("Auth.Forbidden");
        asStaff.ContentType.Should().Be("application/problem+json");
    }
}
