using System.Net;
using System.Text.Json;
using Stockly.Api.IntegrationTests.Support;
using Stockly.Application.Abstractions.Ai;
using Stockly.Domain.Platform;

namespace Stockly.Api.IntegrationTests.Invoices;

/// <summary>
/// Contract I2 <c>GET /api/invoices</c>, I3 <c>GET /api/invoices/{id}</c> and I4 <c>PUT /api/invoices/{id}/items</c> on the
/// real host with PostgreSQL, Redis and MinIO, plus the Sprint 2 flow end to end with the Hangfire server and a fake AI.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class InvoiceReviewEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task ScanJobReviewFlow_MatchedLinesThenEditThenRead()
    {
        await using var factory = fixture.CreateFactory(jobs: StocklyApiFactory.JobMode.Run);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var productId = await ProductApi.CreateAsync(client, session.AccessToken, ProductApi.CreateBody(name: "بنادول اكسترا"));
        factory.Extractor.Result = Extraction(
            ("بنادول إكسترا", "شريط", "10", "12.50"),
            ("فيتامين سي", "علبة", "2", "30.00"));

        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);
        var processed = await InvoiceApi.PollUntilProcessedAsync(client, id, session.AccessToken, TimeSpan.FromSeconds(60));

        var detail = processed.Body!;
        detail["status"]!.GetValue<string>().Should().Be("NeedsReview", processed.Raw);
        detail["computedTotal"]!.GetValue<string>().Should().Be("185.00");
        var items = detail["items"]!.AsArray();
        items.Select(item => item!["lineNo"]!.GetValue<int>()).Should().Equal(1, 2);
        items[0]!["productId"]!.GetValue<Guid>().Should().Be(productId);
        items[0]!["productName"]!.GetValue<string>().Should().Be("بنادول اكسترا");
        items[0]!["unitName"]!.GetValue<string>().Should().Be("شريط");
        items[0]!["matchConfidence"]!.GetValue<string>().Should().Be("0.90");
        items[0]!["quantity"]!.GetValue<string>().Should().Be("10.000");
        items[1]!["productId"].Should().BeNull();
        items[1]!["matchConfidence"].Should().BeNull();
        var unitId = items[0]!["productUnitId"]!.GetValue<Guid>();

        var edited = await InvoiceApi.PutItemsAsync(client, id, new
        {
            supplierName = "شركة الأدوية",
            invoiceNumber = "INV-1",
            invoiceDate = "2026-10-10",
            totalAmount = "500.00",
            items = new object[]
            {
                new { lineNo = 1, rawName = "بنادول إكسترا", productId, productUnitId = unitId, quantity = "10.000", unitPrice = "12.50" },
                new
                {
                    lineNo = 2,
                    rawName = "فيتامين سي",
                    newProduct = new { name = "فيتامين سي", baseUnitName = "علبة", sellPrice = "40.00" },
                    quantity = "2",
                    unitPrice = "30.00",
                    expiryDate = "2027-03-31",
                    batchNo = "B7",
                },
            },
        }, session.AccessToken);

        edited.Status.Should().Be(HttpStatusCode.OK, edited.Raw);
        edited.Body!["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal("TotalMismatch");
        var reread = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(id), session.AccessToken);
        reread.Raw.Should().Be(edited.Raw, "I4 returns the same projection I3 reads");
        var lines = reread.Body!["items"]!.AsArray();
        lines[0]!["matchConfidence"]!.GetValue<string>().Should().Be("0.90", "an unchanged match keeps its confidence");
        lines[1]!["newProduct"]!["sellPrice"]!.GetValue<string>().Should().Be("40.00");
        lines[1]!["expiryDate"]!.GetValue<string>().Should().Be("2027-03-31");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE product_id = @p0", productId))
            .Should().Be(0, "neither the AI nor the review changes stock (rule 19)");
    }

    [Fact]
    public async Task GetInvoice_WhileProcessing_200ProcessingWithEmptyLines()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);

        var response = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(id), session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["status"]!.GetValue<string>().Should().Be("Processing");
        response.Body["items"]!.AsArray().Should().BeEmpty();
        response.Body["clientRequestId"]!.GetValue<Guid>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetInvoice_RedisDown_200FromPostgres()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        await GiveShopAPlanAsync(session.ShopId);
        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);

        var response = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(id), session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["id"]!.GetValue<Guid>().Should().Be(id);
        response.Body["status"]!.GetValue<string>().Should().Be("Processing");
    }

    [Fact]
    public async Task GetInvoice_OtherShopsOrUnknown_404InvoiceNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await InvoiceApi.ScanAsync(client, owner.AccessToken);

        var foreign = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(id), stranger.AccessToken);
        var unknown = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(Guid.CreateVersion7()), owner.AccessToken);

        foreign.Status.Should().Be(HttpStatusCode.NotFound, "wrong tenant is 404, never 403 (rule 10)");
        foreign.Code.Should().Be("Invoice.NotFound");
        unknown.Code.Should().Be("Invoice.NotFound");
    }

    [Fact]
    public async Task ListInvoices_PagedNewestFirstWithStatusFilterAndOnlyThisShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var other = await AuthApi.RegisterAsync(client);
        var first = await InvoiceApi.ScanAsync(client, session.AccessToken);
        var second = await InvoiceApi.ScanAsync(client, session.AccessToken);
        var third = await InvoiceApi.ScanAsync(client, session.AccessToken);
        await InvoiceApi.ScanAsync(client, other.AccessToken);
        await MakeReviewableAsync(second);

        var page = await InvoiceApi.GetAsync(client, $"{InvoiceApi.Invoices}?page=1&pageSize=2", session.AccessToken);
        var filtered = await InvoiceApi.GetAsync(client, $"{InvoiceApi.Invoices}?status=NeedsReview", session.AccessToken);

        page.Status.Should().Be(HttpStatusCode.OK, page.Raw);
        page.Body!["totalCount"]!.GetValue<int>().Should().Be(3);
        page.Body["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Should().Equal(third, second);
        filtered.Body!["items"]!.AsArray().Should().ContainSingle()
            .Which!["itemCount"]!.GetValue<int>().Should().Be(1);
        filtered.Body["items"]![0]!["id"]!.GetValue<Guid>().Should().Be(second);
        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0")]
    [InlineData("?status=Bogus")]
    public async Task ListInvoices_BadQuery_400ValidationFailed(string query)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var response = await InvoiceApi.GetAsync(client, InvoiceApi.Invoices + query, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task UpdateItems_StillProcessing_409NotEditable()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);

        var response = await InvoiceApi.PutItemsAsync(client, id, NewProductBody(), session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Conflict, response.Raw);
        response.Code.Should().Be("Invoice.NotEditable");
    }

    [Fact]
    public async Task UpdateItems_OtherShopsInvoice_404InvoiceNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await InvoiceApi.ScanAsync(client, owner.AccessToken);
        await MakeReviewableAsync(id);

        var response = await InvoiceApi.PutItemsAsync(client, id, NewProductBody(), stranger.AccessToken);

        response.Status.Should().Be(HttpStatusCode.NotFound, response.Raw);
        response.Code.Should().Be("Invoice.NotFound");
    }

    [Fact]
    public async Task UpdateItems_ErrorCases_ContractCodes()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var productId = await ProductApi.CreateAsync(client, session.AccessToken);
        var strangersProduct = await ProductApi.CreateAsync(client, stranger.AccessToken);
        var strangersUnit = (await ProductApi.GetAsync(client, ProductApi.Product(strangersProduct), stranger.AccessToken))
            .Body!["units"]![0]!["id"]!.GetValue<Guid>();
        var otherProduct = await ProductApi.CreateAsync(client, session.AccessToken, ProductApi.CreateBody(name: "صنف آخر"));
        var otherUnit = (await ProductApi.GetAsync(client, ProductApi.Product(otherProduct), session.AccessToken))
            .Body!["units"]![0]!["id"]!.GetValue<Guid>();
        var id = await InvoiceApi.ScanAsync(client, session.AccessToken);
        await MakeReviewableAsync(id);

        var neither = await Put(new { lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00" });
        var both = await Put(new
        {
            lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00", productId, productUnitId = otherUnit,
            newProduct = new { name = "n", baseUnitName = "u", sellPrice = "1.00" },
        });
        var foreignProduct = await Put(new { lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00", productId = strangersProduct, productUnitId = strangersUnit });
        var wrongUnit = await Put(new { lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00", productId, productUnitId = otherUnit });
        var noUnit = await Put(new { lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00", productId, productUnitId = Guid.CreateVersion7() });
        var precision = await Put(new { lineNo = 1, rawName = "x", quantity = "1.0001", unitPrice = "1.00", productId, productUnitId = otherUnit });
        var number = await InvoiceApi.PutItemsRawAsync(
            client, id, $$"""{"items":[{"lineNo":1,"rawName":"x","quantity":1,"unitPrice":"1.00","productId":"{{productId}}","productUnitId":"{{otherUnit}}"}]}""", session.AccessToken);

        (neither.Status, neither.Code).Should().Be((HttpStatusCode.BadRequest, "Invoice.LineProductRequired"));
        (both.Status, both.Code).Should().Be((HttpStatusCode.BadRequest, "Validation.Failed"));
        both.Body!["errors"]!["items[0].newProduct"].Should().NotBeNull(both.Raw);
        (foreignProduct.Status, foreignProduct.Code).Should().Be((HttpStatusCode.NotFound, "Product.NotFound"));
        (wrongUnit.Status, wrongUnit.Code).Should().Be((HttpStatusCode.BadRequest, "Invoice.UnitNotOfProduct"));
        (noUnit.Status, noUnit.Code).Should().Be((HttpStatusCode.NotFound, "ProductUnit.NotFound"));
        (precision.Status, precision.Code).Should().Be((HttpStatusCode.BadRequest, "Precision.TooManyDecimals"));
        (number.Status, number.Code).Should().Be((HttpStatusCode.BadRequest, "Validation.Failed"), "decimals are strings (contract §1.2)");

        Task<ApiResponse> Put(object line) => InvoiceApi.PutItemsAsync(client, id, new { items = new[] { line } }, session.AccessToken);
    }

    private static object NewProductBody() => new
    {
        items = new[]
        {
            new { lineNo = 1, rawName = "x", quantity = "1", unitPrice = "1.00", newProduct = new { name = "n", baseUnitName = "u", sellPrice = "1.00" } },
        },
    };

    private static AiExtractionResult Extraction(params (string Name, string Unit, string Quantity, string UnitPrice)[] lines)
    {
        var extraction = new ExtractedInvoice(
            "شركة الأدوية",
            "INV-1",
            "2026-10-10",
            null,
            false,
            [.. lines.Select(line => new ExtractedInvoiceLine(line.Name, line.Unit, line.Quantity, line.UnitPrice, null, null, null))]);
        return new AiExtractionResult(
            extraction,
            null,
            FakeExtractor.Model,
            JsonSerializer.Serialize(extraction, AiJson.Options),
            [new AiAttempt(FakeExtractor.Model, AiCallOutcome.Succeeded, 200, 1000, 200, 0.0004m, 1200)]);
    }

    // What the job would have written: NeedsReview with one unmatched line.
    private async Task MakeReviewableAsync(Guid invoiceId)
    {
        await fixture.ExecuteAsync("UPDATE invoices SET status = 2, processed_at = now(), computed_total = 1 WHERE id = @p0", invoiceId);
        await fixture.ExecuteAsync(
            "INSERT INTO invoice_items (id, shop_id, invoice_id, line_no, raw_name, quantity, unit_price, line_total) " +
            "SELECT @p0, shop_id, id, 1, 'سطر', 1, 1, 1 FROM invoices WHERE id = @p1",
            Guid.CreateVersion7(),
            invoiceId);
    }

    private async Task GiveShopAPlanAsync(Guid shopId)
    {
        var planId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO plans (id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) " +
            "VALUES (@p0, @p1, 'اختبار', 'Test', 100, 3, 0, true)",
            planId,
            $"test-{planId:N}");
        await fixture.ExecuteAsync("UPDATE shop_subscriptions SET plan_id = @p0 WHERE shop_id = @p1", planId, shopId);
    }
}
