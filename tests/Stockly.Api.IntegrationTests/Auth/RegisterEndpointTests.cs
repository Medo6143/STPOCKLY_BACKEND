using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>A1 <c>POST /api/auth/register</c> against the real Api and PostgreSQL.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class RegisterEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_ValidRequest_Returns201WithOwnerSessionAndLocation()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        var before = DateTimeOffset.UtcNow;

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(email, preferredLanguage: "en"));

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        response.Location!.OriginalString.Should().Be(AuthApi.Me);
        var body = response.Body!;
        body["user"]!["email"]!.GetValue<string>().Should().Be(email);
        body["user"]!["fullName"]!.GetValue<string>().Should().Be("Mona Adel");
        body["user"]!["phone"]!.GetValue<string>().Should().Be("01001234567");
        body["user"]!["preferredLanguage"]!.GetValue<string>().Should().Be("en");
        body["shop"]!["name"]!.GetValue<string>().Should().Be("صيدلية الشفاء");
        body["shop"]!["shopType"]!.GetValue<string>().Should().Be("Pharmacy");
        body["shop"]!["status"]!.GetValue<string>().Should().Be("Active");
        body["shop"]!["role"]!.GetValue<string>().Should().Be("Owner");
        body["refreshToken"]!.GetValue<string>().Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 random bytes, base64url");

        var accessExpiry = body["accessTokenExpiresAt"]!.GetValue<string>();
        var refreshExpiry = body["refreshTokenExpiresAt"]!.GetValue<string>();
        accessExpiry.Should().EndWith("Z");
        refreshExpiry.Should().EndWith("Z");
        DateTimeOffset.Parse(accessExpiry, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeCloseTo(before.AddMinutes(15), TimeSpan.FromSeconds(30));
        DateTimeOffset.Parse(refreshExpiry, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeCloseTo(before.AddDays(30), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Register_AccessToken_CarriesSubShopRoleAndJtiFor15Minutes()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var session = await AuthApi.RegisterAsync(client);
        var token = new JsonWebToken(session.AccessToken);

        token.Alg.Should().Be("HS256");
        token.Issuer.Should().Be("stockly");
        token.Audiences.Should().Equal("stockly-app");
        token.GetClaim("sub").Value.Should().Be(session.UserId.ToString());
        token.GetClaim("shop_id").Value.Should().Be(session.ShopId.ToString());
        token.GetClaim("role").Value.Should().Be("Owner");
        token.GetClaim("jti").Value.Should().NotBeNullOrWhiteSpace();
        (token.ValidTo - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Register_ValidRequest_PersistsShopOwnerMembershipAndActiveDefaultSubscription()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var session = await AuthApi.RegisterAsync(client);

        (await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM shop_members WHERE shop_id = @p0 AND user_id = @p1 AND role = 1 AND is_active", session.ShopId, session.UserId))
            .Should().Be(1);
        (await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM shop_subscriptions s JOIN plans p ON p.id = s.plan_id " +
            "WHERE s.shop_id = @p0 AND s.status = 1 AND s.ends_on IS NULL AND p.code = 'standard' " +
            "AND s.starts_on = (now() AT TIME ZONE 'Africa/Cairo')::date",
            session.ShopId))
            .Should().Be(1);
        (await fixture.ScalarAsync<string>("SELECT device_id FROM refresh_tokens WHERE user_id = @p0", session.UserId))
            .Should().Be("pixel-7");
    }

    [Fact]
    public async Task Register_StoresOnlyTheRefreshTokenHash()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var session = await AuthApi.RegisterAsync(client);

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM refresh_tokens WHERE token_hash = @p0", session.RefreshToken))
            .Should().Be(0, "the token itself is never stored");
        (await fixture.ScalarAsync<string>("SELECT token_hash FROM refresh_tokens WHERE user_id = @p0", session.UserId))
            .Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Register_DuplicateEmailInOtherCase_Returns409EmailTaken()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        await AuthApi.RegisterAsync(client, email);

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(email.ToUpperInvariant()));

        response.Status.Should().Be(HttpStatusCode.Conflict);
        response.Code.Should().Be("Auth.EmailTaken");
        response.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Register_DuplicateEmail_RollsBackTheSecondShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();
        await AuthApi.RegisterAsync(client, email);
        var shopName = $"shop-{Guid.NewGuid():N}";

        await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(email, shopName: shopName));

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shops WHERE name = @p0", shopName)).Should().Be(0);
    }

    [Fact]
    public async Task Register_InvalidFields_Returns400ValidationFailedWithCamelCaseFields()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var body = new
        {
            fullName = "",
            email = "not-an-email",
            password = "short",
            preferredLanguage = "fr",
            shop = new { name = (string?)null, shopType = "Pharmacy" },
        };

        var response = await AuthApi.PostAsync(client, AuthApi.Register, body);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!.AsObject().Select(pair => pair.Key)
            .Should().BeEquivalentTo("fullName", "email", "password", "preferredLanguage", "shop.name");
    }

    [Theory]
    [InlineData("abcdefgh")] // no digit
    [InlineData("12345678")] // no letter
    [InlineData("abc1234")] // 7 characters
    [InlineData("كلمةسر١٢٣٤")] // Arabic-Indic digits only: Identity counts 0-9
    public async Task Register_WeakPassword_Returns400OnPassword(string weak)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(AuthApi.NewEmail(), password: weak));

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!["password"].Should().NotBeNull();
    }

    [Fact]
    public async Task Register_ArabicLetterPassword_IsAccepted()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(AuthApi.NewEmail(), password: "كلمةسر1234"));

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
    }

    [Fact]
    public async Task Register_MissingShop_Returns400OnShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Register, new { fullName = "Mona", email = AuthApi.NewEmail(), password = AuthApi.Password });

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!["shop"].Should().NotBeNull();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("َـ")] // only tashkeel and tatweel
    public async Task Register_ShopNameBlankAfterNormalization_Returns400ShopNameRequired(string name)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(AuthApi.NewEmail(), shopName: name));

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Shop.NameRequired");
    }

    [Theory]
    [InlineData("Bakery")]
    [InlineData("pharmacy")]
    [InlineData("1")]
    public async Task Register_UnsupportedShopType_Returns400ShopUnknownType(string shopType)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(AuthApi.NewEmail(), shopType: shopType));

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Shop.UnknownType");
    }
}
