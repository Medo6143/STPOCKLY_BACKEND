using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Admin.Components;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Billing;

namespace Stockly.Admin.Tests.Pages;

/// <summary>The Payments page (AD21), the overview revenue card (AD22) and the shop's payments, each in its four states.</summary>
public sealed class PaymentsPageTests : AdminTestContext
{
    [Fact]
    public void Payments_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.Payments);

        var page = Render<Payments>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Payments_None_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.Payments, Samples.Page<AdminPayment>());

        var page = Render<Payments>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No payments");
    }

    [Fact]
    public void Payments_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.Payments, HttpStatusCode.InternalServerError, "Server.Unexpected", "Something failed", Samples.TraceId);

        var page = Render<Payments>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Something failed").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Payments_Data_ShowsShopLinkAmountStatusAndTransaction()
    {
        Api.OnGet(ApiRoutes.Payments, Samples.Page(Samples.Payment(), Samples.Payment(PaymentStatus.Failed, PaymentMethod.Wallet)));

        var page = Render<Payments>();

        page.WaitForAssertion(() => page.Find("[data-testid=payments-table]").TextContent
            .Should().Contain("199.50 EGP").And.Contain("192036465").And.Contain("Paid").And.Contain("Failed")
            .And.Contain("Do not honour").And.Contain("Wallet"));
        page.Find($"a[href='{AppRoutes.Shop(Samples.ShopId)}']").TextContent.Should().Contain("صيدلية النور");
    }

    [Fact]
    public void Payments_ShopFilterInTheAddress_FiltersByShop()
    {
        Api.OnGet(ApiRoutes.Payments, Samples.Page(Samples.Payment()));
        Services.GetRequiredService<NavigationManager>().NavigateTo(AppRoutes.PaymentsOfShop(Samples.ShopId));

        var page = Render<Payments>();

        page.WaitForAssertion(() => page.Find("[data-testid=payments-table]").Should().NotBeNull());
        Api.To(HttpMethod.Get, ApiRoutes.Payments).Last().Query.Should().Contain($"shopId={Samples.ShopId}");
        page.Find("[data-testid=shop-filter]").Should().NotBeNull();
    }

    [Fact]
    public void Revenue_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.PaymentSummary);

        var card = Render<RevenueSummary>();

        card.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Revenue_NoPaidPayments_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.PaymentSummary, Samples.Revenue(withPayments: false));

        var card = Render<RevenueSummary>();

        card.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No paid plans yet");
    }

    [Fact]
    public void Revenue_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.PaymentSummary, HttpStatusCode.BadRequest, "Validation.Failed", "Bad range", Samples.TraceId);

        var card = Render<RevenueSummary>();

        card.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Bad range").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Revenue_Data_ShowsTotalAndByMethodFromTheDecimalStrings()
    {
        Api.OnGet(ApiRoutes.PaymentSummary, Samples.Revenue());

        var card = Render<RevenueSummary>();

        card.WaitForAssertion(() => card.Find("[data-testid=revenue-amount]").TextContent.Should().Contain("1,250.50 EGP"));
        card.FindAll("[data-testid=revenue-method]").Should().HaveCount(2);
        card.Markup.Should().Contain("250.50 EGP").And.Contain("Kiosk");
        Api.To(HttpMethod.Get, ApiRoutes.PaymentSummary).Single().Query.Should().Contain("from=").And.Contain("to=");
    }

    [Fact]
    public void ShopPayments_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.Payments);

        var section = RenderShopPayments();

        section.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void ShopPayments_None_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.Payments, Samples.Page<AdminPayment>());

        var section = RenderShopPayments();

        section.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("has not paid");
    }

    [Fact]
    public void ShopPayments_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.Payments, HttpStatusCode.InternalServerError, "Server.Unexpected", "Something failed", Samples.TraceId);

        var section = RenderShopPayments();

        section.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain(Samples.TraceId);
    }

    [Fact]
    public void ShopPayments_Data_AsksForTheShopAndHidesTheShopColumn()
    {
        Api.OnGet(ApiRoutes.Payments, Samples.Page(Samples.Payment()));

        var section = RenderShopPayments();

        section.WaitForAssertion(() => section.Find("[data-testid=payments-table]").TextContent.Should().Contain("199.50 EGP"));
        section.Find("[data-testid=payments-table]").TextContent.Should().NotContain("صيدلية النور");
        Api.To(HttpMethod.Get, ApiRoutes.Payments).Single().Query.Should().Contain($"shopId={Samples.ShopId}");
    }

    private IRenderedComponent<ShopPayments> RenderShopPayments() =>
        Render<ShopPayments>(parameters => parameters.Add(component => component.ShopId, Samples.ShopId));
}
