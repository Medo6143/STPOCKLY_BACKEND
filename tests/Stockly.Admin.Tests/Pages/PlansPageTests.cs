using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class PlansPageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.Plans);

        var page = Render<Plans>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NoPlans_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.Plans, Array.Empty<PlanDto>());

        var page = Render<Plans>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No plans yet");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.Forbidden, "Auth.Forbidden", "Not allowed", Samples.TraceId);

        var page = Render<Plans>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Not allowed").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_Plans_ShowsPriceFromTheDecimalString()
    {
        Api.OnGet(ApiRoutes.Plans, new[] { Samples.Plan(), Samples.Plan(isActive: false, code: "legacy") });

        var page = Render<Plans>();

        page.WaitForAssertion(() => page.Find("[data-testid=plans-table]").TextContent
            .Should().Contain("199.50 EGP").And.Contain("legacy").And.Contain("Inactive"));
    }

    [Fact]
    public void Create_CodeTaken_KeepsDialogOpenWithTheProblem()
    {
        Api.OnGet(ApiRoutes.Plans, Array.Empty<PlanDto>());
        Api.Problem(HttpMethod.Post, ApiRoutes.Plans, HttpStatusCode.Conflict, "Plan.CodeTaken", "A plan with this code already exists", Samples.TraceId);
        var page = Render<Plans>();
        page.Find("[data-testid=new-plan]").Click();

        Dialogs.WaitForElement("[data-testid=plan-code]").Input("pro");
        Dialogs.Find("[data-testid=plan-name]").Input("Pro");
        Dialogs.Find("[data-testid=plan-price]").Input("299.00");
        Dialogs.Find("[data-testid=plan-save]").Click();

        Dialogs.WaitForAssertion(() => Dialogs.Find("[data-testid=state-error]").TextContent.Should().Contain("A plan with this code already exists"));
        Api.To(HttpMethod.Post, ApiRoutes.Plans).Single().Body.Should().Contain("\"monthlyPrice\":\"299.00\"").And.Contain("\"code\":\"pro\"");
    }
}
