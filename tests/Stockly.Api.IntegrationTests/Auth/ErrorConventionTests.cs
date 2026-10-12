using System.Net;
using System.Text;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>Contract §1.5 and §1.8 on real requests: localized text, stable codes, framework <c>Http.*</c> codes.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ErrorConventionTests(ApiFixture fixture)
{
    [Fact]
    public async Task AcceptLanguage_EnVsAr_ChangesDetailButNotCode()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var body = new { email = AuthApi.NewEmail(), password = "Wr0ngPass" };

        var english = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "en");
        var arabic = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "ar");
        var noHeader = await AuthApi.PostAsync(client, AuthApi.Login, body);

        english.Code.Should().Be("Auth.InvalidCredentials");
        arabic.Code.Should().Be("Auth.InvalidCredentials");
        english.Body!["detail"]!.GetValue<string>().Should().Be("The email or password is incorrect.");
        arabic.Body!["detail"]!.GetValue<string>().Should().Be("البريد الإلكتروني أو كلمة المرور غير صحيحة.");
        noHeader.Body!["detail"]!.GetValue<string>().Should().Be(arabic.Body!["detail"]!.GetValue<string>(), "Arabic is the default");
    }

    [Fact]
    public async Task AcceptLanguage_RegionalVariant_FallsBackToItsLanguage()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var body = new { email = AuthApi.NewEmail(), password = "Wr0ngPass" };

        var english = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "en-GB");
        var arabic = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "ar-EG");

        english.Body!["detail"]!.GetValue<string>().Should().Be("The email or password is incorrect.");
        arabic.Body!["detail"]!.GetValue<string>().Should().Be("البريد الإلكتروني أو كلمة المرور غير صحيحة.");
    }

    [Fact]
    public async Task ValidationMessages_FollowAcceptLanguage_FieldKeysDoNot()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var body = AuthApi.RegisterBody(AuthApi.NewEmail(), password: "abcdefgh");

        var english = await AuthApi.PostAsync(client, AuthApi.Register, body, language: "en");
        var arabic = await AuthApi.PostAsync(client, AuthApi.Register, body, language: "ar");

        var englishMessage = english.Body!["errors"]!["password"]![0]!.GetValue<string>();
        var arabicMessage = arabic.Body!["errors"]!["password"]![0]!.GetValue<string>();
        englishMessage.Should().Be("Password must contain at least one digit (0-9).");
        arabicMessage.Should().Be("يجب أن تحتوي كلمة المرور على رقم واحد على الأقل (0-9).");
        english.Body!["title"]!.GetValue<string>().Should().NotBe(arabic.Body!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task FluentValidationBuiltInMessages_AreLocalized()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var body = new { email = "", password = "x" };

        var english = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "en");
        var arabic = await AuthApi.PostAsync(client, AuthApi.Login, body, language: "ar");

        english.Body!["errors"]!["email"]![0]!.GetValue<string>()
            .Should().NotBe(arabic.Body!["errors"]!["email"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task MalformedJson_Returns400ValidationFailedWithoutParserDetails()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AuthApi.Login, UriKind.Relative))
        {
            Content = new StringContent("{\"email\": 42, \"password\": ", Encoding.UTF8, "application/json"),
        };

        var response = await AuthApi.SendAsync(client, request, language: "en");

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!.AsObject().Should().NotBeEmpty();
        response.Raw.Should().NotContain("Stockly.Contracts").And.NotContain("LineNumber");
    }

    [Fact]
    public async Task UnknownRoute_Returns404HttpNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri("/api/nope", UriKind.Relative)), language: "en");

        response.Status.Should().Be(HttpStatusCode.NotFound);
        response.Code.Should().Be("Http.NotFound");
        response.Body!["title"]!.GetValue<string>().Should().Be("Not found");
    }

    [Fact]
    public async Task WrongMethod_Returns405HttpMethodNotAllowed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri(AuthApi.Login, UriKind.Relative)));

        response.Status.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Code.Should().Be("Http.MethodNotAllowed");
        response.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task WrongContentType_Returns415HttpUnsupportedMediaType()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AuthApi.Login, UriKind.Relative))
        {
            Content = new StringContent("email=a", Encoding.UTF8, "text/plain"),
        };

        var response = await AuthApi.SendAsync(client, request);

        response.Status.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.Code.Should().Be("Http.UnsupportedMediaType");
    }
}
