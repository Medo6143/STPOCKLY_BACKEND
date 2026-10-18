using System.Net;
using Bunit;
using Stockly.Admin.Components;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Components;

public sealed class HangfireButtonTests : AdminTestContext
{
    [Fact]
    public void Click_OpensTheOneTimeUrlOnTheApiHostInANewTab()
    {
        Api.On(HttpMethod.Post, ApiRoutes.HangfireSession, HttpStatusCode.OK, new HangfireSessionResponse("/hangfire?code=abc"));
        var open = JSInterop.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var button = Render<HangfireButton>();

        button.Find("[data-testid=open-hangfire]").Click();

        button.WaitForAssertion(() => open.Invocations.Should().ContainSingle());
        open.Invocations.Single().Arguments.Should().Equal("http://api.test/hangfire?code=abc", "_blank", "noopener");
        Api.To(HttpMethod.Post, ApiRoutes.HangfireSession).Single().BearerToken.Should().Be(AccessToken);
    }
}
