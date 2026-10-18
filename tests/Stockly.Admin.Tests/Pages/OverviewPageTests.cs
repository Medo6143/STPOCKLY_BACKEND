using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class OverviewPageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.Shops);

        var page = Render<Overview>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NothingOnThePlatform_ShowsEmptyState()
    {
        Arrange(Samples.Page<AdminShopListItem>(), Samples.Days(withUsage: false), Samples.Page<AdminFailedInvoice>());

        var page = Render<Overview>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No shops yet");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.UsageByModel, HttpStatusCode.InternalServerError, "Server.Unexpected", "Something failed", Samples.TraceId);
        Arrange(Samples.Page(Samples.ShopRow()), Samples.Days(withUsage: true), Samples.Page(Samples.FailedRow()));

        var page = Render<Overview>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Something failed").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_Data_ShowsTotalsWithExactDecimalSum()
    {
        Arrange(Samples.Page(Samples.ShopRow()), Samples.Days(withUsage: true), Samples.Page(Samples.FailedRow()));

        var page = Render<Overview>();

        // 30 days × $0.00012345 = $0.00370350, summed as decimal.
        page.WaitForAssertion(() => page.Markup.Should().Contain("$0.00370350"));
        page.Markup.Should().Contain("AI output failed validation").And.Contain("google/gemini-2.5-flash");
        page.FindAll("[data-testid=usage-chart]").Should().NotBeEmpty();
    }

    private void Arrange(object shops, IReadOnlyList<AiUsageDay> days, object failed)
    {
        Api.OnGet(ApiRoutes.Shops, shops);
        Api.OnGet(ApiRoutes.DailyUsage, days);
        Api.OnGet(ApiRoutes.UsageByModel, new[] { Samples.UsageByModel() });
        Api.OnGet(ApiRoutes.FailedInvoices, failed);
    }
}
