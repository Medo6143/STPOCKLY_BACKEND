using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Auth;

/// <summary>A2 account lockout (contract §1.4, v1.4): 5 failed logins lock an existing account for 15 minutes.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class LockoutEndpointTests(ApiFixture fixture)
{
    private const int MaxFailures = 5;
    private const string WrongPassword = "Wr0ngPass";
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_CorrectPasswordReturns423WithRetryAfter_UntilFifteenMinutesPass()
    {
        var clock = new TestClock(Start);
        await using var factory = fixture.CreateFactory(clock: clock);
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        for (var attempt = 1; attempt <= MaxFailures; attempt++)
        {
            var failed = await LoginAsync(client, registered.Email, WrongPassword);
            failed.Code.Should().Be("Auth.InvalidCredentials", "attempt {0} is counted, not yet locked", attempt);
        }

        var locked = await LoginAsync(client, registered.Email, AuthApi.Password);

        locked.Status.Should().Be(HttpStatusCode.Locked, locked.Raw);
        locked.Code.Should().Be("Auth.LockedOut");
        locked.ContentType.Should().Be("application/problem+json");
        locked.RetryAfter.Should().Be("900", "whole seconds until the lockout ends");

        clock.UtcNow = Start.AddMinutes(14).AddSeconds(59.5);
        var almost = await LoginAsync(client, registered.Email, AuthApi.Password);
        almost.Status.Should().Be(HttpStatusCode.Locked);
        almost.RetryAfter.Should().Be("1", "Retry-After is rounded up and at least 1");

        clock.UtcNow = Start.AddMinutes(15).AddSeconds(1);
        var afterLockout = await LoginAsync(client, registered.Email, AuthApi.Password);
        afterLockout.Status.Should().Be(HttpStatusCode.OK, afterLockout.Raw);
    }

    [Fact]
    public async Task Login_SuccessBetweenFailures_ResetsTheCount()
    {
        await using var factory = fixture.CreateFactory(clock: new TestClock(Start));
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);

        await FailAsync(client, registered.Email, MaxFailures - 1);
        (await LoginAsync(client, registered.Email, AuthApi.Password)).Status.Should().Be(HttpStatusCode.OK);
        await FailAsync(client, registered.Email, MaxFailures - 1);

        var response = await LoginAsync(client, registered.Email, AuthApi.Password);

        response.Status.Should().Be(HttpStatusCode.OK, "4 failures, a success, 4 failures never reach 5 in a row");
    }

    [Fact]
    public async Task Login_UnknownEmail_NeverLocked()
    {
        await using var factory = fixture.CreateFactory(clock: new TestClock(Start));
        using var client = factory.CreateClient();
        var email = AuthApi.NewEmail();

        for (var attempt = 0; attempt <= MaxFailures + 1; attempt++)
        {
            var response = await LoginAsync(client, email, AuthApi.Password);
            response.Status.Should().Be(HttpStatusCode.Unauthorized);
            response.Code.Should().Be("Auth.InvalidCredentials");
            response.RetryAfter.Should().BeNull();
        }
    }

    [Fact]
    public async Task Login_LockedOut_LocalizedInArabicAndEnglish()
    {
        await using var factory = fixture.CreateFactory(clock: new TestClock(Start));
        using var client = factory.CreateClient();
        var registered = await AuthApi.RegisterAsync(client);
        await FailAsync(client, registered.Email, MaxFailures);

        var arabic = await AuthApi.PostAsync(client, AuthApi.Login, new { email = registered.Email, password = AuthApi.Password }, "ar");
        var english = await AuthApi.PostAsync(client, AuthApi.Login, new { email = registered.Email, password = AuthApi.Password }, "en");

        arabic.Code.Should().Be("Auth.LockedOut");
        english.Code.Should().Be("Auth.LockedOut");
        arabic.Body!["detail"]!.GetValue<string>().Should().NotBe(english.Body!["detail"]!.GetValue<string>());
    }

    private static Task<ApiResponse> LoginAsync(HttpClient client, string email, string password) =>
        AuthApi.PostAsync(client, AuthApi.Login, new { email, password });

    private static async Task FailAsync(HttpClient client, string email, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            (await LoginAsync(client, email, WrongPassword)).Status.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
