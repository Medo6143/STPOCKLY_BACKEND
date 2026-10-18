using System.Net;
using Bunit;
using Stockly.Admin.Pages;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Invoices;

namespace Stockly.Admin.Tests.Pages;

public sealed class FailedScanDetailPageTests : AdminTestContext
{
    private static readonly string InvoicePath = ApiRoutes.Invoice(Samples.InvoiceId);

    [Fact]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        Api.Pending(HttpMethod.Get, InvoicePath);

        var page = RenderPage();

        page.Find("[data-testid=state-loading]").Should().NotBeNull();
    }

    [Fact]
    public void Render_ImageAndAiOutputGone_ShowsEmptySections()
    {
        Api.OnGet(InvoicePath, Samples.InvoiceDetail(imageUrl: null, raw: null));

        var page = RenderPage();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Photo not available").And.Contain("No AI output stored"));
        page.FindAll("[data-testid=scan-image]").Should().BeEmpty();
    }

    [Fact]
    public void Render_UnknownInvoice_ShowsNotFoundProblem()
    {
        Api.Problem(HttpMethod.Get, InvoicePath, HttpStatusCode.NotFound, "Invoice.NotFound", "The invoice was not found", Samples.TraceId);

        var page = RenderPage();

        page.WaitForElement("[data-testid=state-error]").TextContent.Should().Contain("The invoice was not found").And.Contain(Samples.TraceId);
    }

    [Fact]
    public void Render_Detail_ShowsImageRawJsonAndUsageCost()
    {
        Api.OnGet(InvoicePath, Samples.InvoiceDetail());

        var page = RenderPage();

        page.WaitForElement("[data-testid=scan-image]").GetAttribute("src").Should().Be("http://minio.test/stockly-invoices/a.jpg");
        page.Find("[data-testid=json-view]").TextContent.Should().Contain("\"items\": []");
        page.Find("[data-testid=usage-rows]").TextContent.Should().Contain("$" + Samples.Cost);
    }

    [Fact]
    public void Rerun_WithReason_PostsReasonAndReloads()
    {
        Api.OnGet(InvoicePath, Samples.InvoiceDetail());
        Api.OnGet(InvoicePath, Samples.InvoiceDetail() with { Invoice = Samples.Invoice(InvoiceStatus.Processing) });
        Api.On(HttpMethod.Post, ApiRoutes.Rerun(Samples.InvoiceId), HttpStatusCode.Accepted, Samples.Invoice(InvoiceStatus.Processing));
        var page = RenderPage();
        page.WaitForElement("[data-testid=rerun]").Click();

        Dialogs.WaitForElement("textarea").Input("Model fixed");
        Dialogs.Find("[data-testid=reason-confirm]").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=invoice-status]").TextContent.Should().Contain("Processing"));
        Api.To(HttpMethod.Post, ApiRoutes.Rerun(Samples.InvoiceId)).Single().Body.Should().Contain("\"reason\":\"Model fixed\"");
    }

    private IRenderedComponent<FailedScanDetail> RenderPage() =>
        Render<FailedScanDetail>(parameters => parameters.Add(page => page.Id, Samples.InvoiceId));
}
