using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class LoginPageTests : AdminTestContext
{
    public LoginPageTests() => Session.SignOut();

    [Fact]
    public void Submit_WhileWaiting_ShowsProgress()
    {
        Api.Pending(HttpMethod.Post, ApiRoutes.Login);
        var page = Render<Login>();

        Submit(page, "admin@example.com", "secret-1");

        page.WaitForElement("[data-testid=state-loading]");
        page.Find("[data-testid=login-submit]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Submit_EmptyFields_DoesNotCallTheApi()
    {
        var page = Render<Login>();

        page.Find("form").Submit();

        page.Markup.Should().Contain("Enter your email.");
        Api.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Submit_WrongPassword_ShowsProblemAndStaysSignedOut()
    {
        Api.Problem(HttpMethod.Post, ApiRoutes.Login, HttpStatusCode.Unauthorized, "Auth.InvalidCredentials", "Wrong email or password", Samples.TraceId);
        var page = Render<Login>();

        Submit(page, "admin@example.com", "wrong-1");

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Wrong email or password").And.Contain(Samples.TraceId);
        Session.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public void Submit_ValidCredentials_SignsInAndGoesToReturnUrl()
    {
        Api.On(HttpMethod.Post, ApiRoutes.Login, HttpStatusCode.OK, new AdminAuthResponse(
            "access-new", DateTime.UtcNow, "refresh-new", DateTime.UtcNow, new AdminUserDto(Guid.NewGuid(), "admin@example.com", "Admin")));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(AppRoutes.LoginReturningTo(AppRoutes.Plans));
        var page = Render<Login>();

        Submit(page, "admin@example.com", "secret-1");

        page.WaitForAssertion(() => Session.AccessToken.Should().Be("access-new"));
        navigation.Uri.Should().EndWith("/" + AppRoutes.Plans);
    }

    [Fact]
    public void Submit_AbsoluteReturnUrl_GoesToTheOverviewInstead()
    {
        Api.On(HttpMethod.Post, ApiRoutes.Login, HttpStatusCode.OK, new AdminAuthResponse(
            "access-new", DateTime.UtcNow, "refresh-new", DateTime.UtcNow, new AdminUserDto(Guid.NewGuid(), "admin@example.com", "Admin")));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(AppRoutes.LoginReturningTo("https://evil.example/"));
        var page = Render<Login>();

        Submit(page, "admin@example.com", "secret-1");

        page.WaitForAssertion(() => Session.IsSignedIn.Should().BeTrue());
        navigation.Uri.Should().Be(navigation.BaseUri);
    }

    private static void Submit(IRenderedComponent<Login> page, string email, string password)
    {
        page.Find("[data-testid=login-email]").Change(email);
        page.Find("[data-testid=login-password]").Change(password);
        page.Find("form").Submit();
    }
}
