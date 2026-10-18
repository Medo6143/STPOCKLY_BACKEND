using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class FailedScansPageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.FailedInvoices);

        var page = Render<FailedScans>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NoFailures_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.FailedInvoices, Samples.Page<AdminFailedInvoice>());

        var page = Render<FailedScans>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No failed scans");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.FailedInvoices, HttpStatusCode.InternalServerError, "Server.Unexpected", "Something failed", Samples.TraceId);

        var page = Render<FailedScans>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Something failed").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_ShopFilterInTheAddress_FiltersByShop()
    {
        Api.OnGet(ApiRoutes.FailedInvoices, Samples.Page(Samples.FailedRow()));
        Services.GetRequiredService<NavigationManager>().NavigateTo(AppRoutes.FailedScansOfShop(Samples.ShopId));

        var page = Render<FailedScans>();

        page.WaitForAssertion(() => page.Find("[data-testid=failed-table]").TextContent.Should().Contain("AI output failed validation"));
        Api.To(HttpMethod.Get, ApiRoutes.FailedInvoices).Last().Query.Should().Contain($"shopId={Samples.ShopId}");
        page.Find("[data-testid=shop-filter]").Should().NotBeNull();
    }
}
