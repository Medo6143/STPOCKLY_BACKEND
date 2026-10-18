using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class AiUsagePageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.DailyUsage);

        var page = Render<AiUsage>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NoUsage_ShowsEmptyState()
    {
        Arrange(Samples.Days(withUsage: false), Samples.Page<AiUsageByShop>(), []);

        var page = Render<AiUsage>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No AI usage in this range");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.DailyUsage, HttpStatusCode.BadRequest, "Validation.Failed", "The range is too long", Samples.TraceId);
        Arrange(Samples.Days(withUsage: true), Samples.Page(Samples.UsageByShop()), [Samples.UsageByModel()]);

        var page = Render<AiUsage>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("The range is too long").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_Usage_ShowsCostsWithEightDecimalsUnchanged()
    {
        Arrange(Samples.Days(withUsage: true), Samples.Page(Samples.UsageByShop()), [Samples.UsageByModel()]);

        var page = Render<AiUsage>();

        page.WaitForAssertion(() => page.Find("[data-testid=usage-by-shop]").TextContent.Should().Contain("$" + Samples.Cost));
        page.Find("[data-testid=usage-by-model]").TextContent.Should().Contain("$" + Samples.Cost).And.Contain("2,400 ms");
        page.Markup.Should().Contain("$0.00370350");
    }

    [Fact]
    public void Render_DefaultRange_RequestsTheLastThirtyDays()
    {
        Arrange(Samples.Days(withUsage: true), Samples.Page(Samples.UsageByShop()), [Samples.UsageByModel()]);

        var page = Render<AiUsage>();

        page.WaitForAssertion(() => Api.To(HttpMethod.Get, ApiRoutes.DailyUsage).Should().ContainSingle());
        Api.To(HttpMethod.Get, ApiRoutes.DailyUsage).Single().Query.Should().Be("?from=2026-09-19&to=2026-10-18");
    }

    private void Arrange(IReadOnlyList<AiUsageDay> days, object byShop, AiUsageByModel[] byModel)
    {
        Api.OnGet(ApiRoutes.DailyUsage, days);
        Api.OnGet(ApiRoutes.UsageByShop, byShop);
        Api.OnGet(ApiRoutes.UsageByModel, byModel);
    }
}
