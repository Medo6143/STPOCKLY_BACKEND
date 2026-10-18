using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class ShopsPageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.Shops);

        var page = Render<Shops>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NoShops_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.Shops, Samples.Page<AdminShopListItem>());

        var page = Render<Shops>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No shops found");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.Shops, HttpStatusCode.BadRequest, "Validation.Failed", "pageSize is too large", Samples.TraceId);

        var page = Render<Shops>();

        var error = page.WaitForElement("[data-testid=state-error]");
        error.TextContent.Should().Contain("pageSize is too large").And.Contain(Samples.TraceId).And.Contain("Validation.Failed");
    }

    [Fact]
    public void Render_Shops_ShowsRowsWithTheBearerToken()
    {
        Api.OnGet(ApiRoutes.Shops, Samples.Page(Samples.ShopRow()));

        var page = Render<Shops>();

        page.WaitForAssertion(() => page.Find("[data-testid=shops-table]").TextContent
            .Should().Contain("صيدلية النور").And.Contain("owner@example.com").And.Contain("standard"));
        var request = Api.To(HttpMethod.Get, ApiRoutes.Shops).Single();
        request.BearerToken.Should().Be(AccessToken);
        request.Query.Should().Be("?page=1&pageSize=20");
    }
}
