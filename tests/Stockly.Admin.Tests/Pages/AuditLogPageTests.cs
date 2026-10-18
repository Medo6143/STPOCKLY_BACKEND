using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Pages;

public sealed class AuditLogPageTests : AdminTestContext
{
    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, ApiRoutes.AuditLogs);

        var page = Render<AuditLog>();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_NoEntries_ShowsEmptyState()
    {
        Api.OnGet(ApiRoutes.AuditLogs, Samples.Page<AuditLogEntry>());

        var page = Render<AuditLog>();

        page.WaitForElement("[data-testid=state-empty]").TextContent.Should().Contain("No audit entries");
    }

    [Fact]
    public void Render_ApiError_ShowsDetailAndTraceId()
    {
        Api.Problem(HttpMethod.Get, ApiRoutes.AuditLogs, HttpStatusCode.BadRequest, "Validation.Failed", "Bad filter", Samples.TraceId);

        var page = Render<AuditLog>();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("Bad filter").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_EntriesForATarget_FiltersAndShowsChanges()
    {
        Api.OnGet(ApiRoutes.AuditLogs, Samples.Page(Samples.Audit()));
        Services.GetRequiredService<NavigationManager>().NavigateTo(AppRoutes.AuditLogOfTarget(AuditTargetTypes.Shop, Samples.ShopId));

        var page = Render<AuditLog>();

        page.WaitForAssertion(() => page.Find("[data-testid=audit-table]").TextContent
            .Should().Contain("Shop.Suspend").And.Contain("Unpaid invoice").And.Contain("admin@example.com"));
        Api.To(HttpMethod.Get, ApiRoutes.AuditLogs).Last().Query.Should().Be($"?targetType=Shop&targetId={Samples.ShopId}&page=1&pageSize=20");

        page.Find("button[aria-label='Show changes']").Click();

        page.FindAll("[data-testid=json-view]").Select(view => view.TextContent).Should()
            .Contain(text => text.Contains("\"Active\"")).And.Contain(text => text.Contains("\"Suspended\""));
    }
}
