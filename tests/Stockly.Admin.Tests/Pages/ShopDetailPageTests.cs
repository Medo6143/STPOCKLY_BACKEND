using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Shops;

namespace Stockly.Admin.Tests.Pages;

public sealed class ShopDetailPageTests : AdminTestContext
{
    private static readonly string ShopPath = ApiRoutes.Shop(Samples.ShopId);

    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ShopPath);

        var page = RenderPage();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_ShopWithoutMembersOrPlan_ShowsEmptySections()
    {
        Api.OnGet(ShopPath, Samples.Shop(withMembers: false, withSubscription: false));

        var page = RenderPage();

        page.WaitForAssertion(() => page.FindAll("[data-testid=state-empty]").Should().HaveCount(2));
        page.Markup.Should().Contain("No active subscription").And.Contain("No members");
    }

    [Fact]
    public void Render_UnknownShop_ShowsNotFoundProblem()
    {
        Api.Problem(HttpMethod.Get, ShopPath, HttpStatusCode.NotFound, "Shop.NotFound", "The shop was not found", Samples.TraceId);

        var page = RenderPage();

        page.WaitForElement("[data-testid=state-error]").TextContent
            .Should().Contain("The shop was not found").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_Shop_ShowsCostWithEightDecimalsUnchanged()
    {
        Api.OnGet(ShopPath, Samples.Shop());

        var page = RenderPage();

        page.WaitForAssertion(() => page.Markup.Should().Contain("$" + Samples.Cost));
        page.Find("[data-testid=subscription]").TextContent.Should().Contain("Standard").And.Contain("standard");
        page.Markup.Should().Contain("Mona Owner");
    }

    [Fact]
    public void Suspend_WithReason_PostsReasonAndReloads()
    {
        Api.OnGet(ShopPath, Samples.Shop());
        Api.OnGet(ShopPath, Samples.Shop(ShopStatus.Suspended));
        Api.On(HttpMethod.Post, ApiRoutes.SuspendShop(Samples.ShopId), HttpStatusCode.NoContent);
        var page = RenderPage();
        page.WaitForElement("[data-testid=suspend-shop]").Click();

        Dialogs.WaitForElement("textarea").Input("Unpaid invoice");
        Dialogs.Find("[data-testid=reason-confirm]").Click();

        page.WaitForElement("[data-testid=activate-shop]");
        Api.To(HttpMethod.Post, ApiRoutes.SuspendShop(Samples.ShopId)).Single().Body.Should().Contain("\"reason\":\"Unpaid invoice\"");
    }

    [Fact]
    public void Suspend_BlankReason_CannotConfirmAndSendsNothing()
    {
        Api.OnGet(ShopPath, Samples.Shop());
        var page = RenderPage();
        page.WaitForElement("[data-testid=suspend-shop]").Click();

        Dialogs.WaitForElement("textarea").Input("   ");

        Dialogs.Find("[data-testid=reason-confirm]").HasAttribute("disabled").Should().BeTrue();
        Api.To(HttpMethod.Post, ApiRoutes.SuspendShop(Samples.ShopId)).Should().BeEmpty();
    }

    [Fact]
    public void ResetQuota_ApiError_ShowsProblemInSnackbar()
    {
        Api.OnGet(ShopPath, Samples.Shop());
        Api.Problem(HttpMethod.Post, ApiRoutes.QuotaReset(Samples.ShopId), HttpStatusCode.BadRequest, "Admin.ReasonRequired", "Reason required", Samples.TraceId);
        var snackbars = Render<MudBlazor.MudSnackbarProvider>();
        var page = RenderPage();
        page.WaitForElement("[data-testid=reset-quota]").Click();

        Dialogs.WaitForElement("textarea").Input("Support ticket 12");
        Dialogs.Find("[data-testid=reason-confirm]").Click();

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Reason required").And.Contain(Samples.TraceId));
    }

    private IRenderedComponent<ShopDetail> RenderPage() =>
        Render<ShopDetail>(parameters => parameters.Add(page => page.Id, Samples.ShopId));
}
