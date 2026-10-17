using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Invoices;

/// <summary>
/// Contract I5 <c>POST /api/invoices/{id}/confirm</c> on the real host with PostgreSQL and Redis (docs/design/backend.md §11):
/// stock received in base units, new products, supplier, aliases, idempotency and parallel confirms.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ConfirmEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Confirm_ReviewedInvoice_ReceivesBaseQuantitiesCreatesProductSupplierAndAliases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (panadol, strip) = await StockFlowApi.ProductAsync(client, session.AccessToken, "بنادول", openingQuantity: "4.000");
        var box = await StockFlowApi.UnitAsync(client, session.AccessToken, panadol, "علبة", "3.000000", "40.00");
        var listed = await ProductApi.GetAsync(client, ProductApi.Products, session.AccessToken);
        listed.Status.Should().Be(HttpStatusCode.OK);
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(
            fixture,
            session,
            "شركة الأدوية",
            new ReviewLine(1, "بنادول علبة", "2.000", "100.00", panadol, box),
            new ReviewLine(2, "بنادول شريط", "1.000", "12.00", panadol, strip),
            new ReviewLine(3, "فيتامين سي", "4.000", "30.00", NewName: "فيتامين سي", NewBaseUnit: "علبة", NewSellPrice: "40.00"),
            new ReviewLine(4, "فيتامين  سى", "1.000", "35.00", NewName: "فيتامين سى", NewBaseUnit: "علبه", NewSellPrice: "45.00"));

        var response = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        var body = response.Body!;
        body["status"]!.GetValue<string>().Should().Be("Confirmed");
        body["confirmedAt"].Should().NotBeNull();
        body["supplierId"].Should().NotBeNull();
        (await StockFlowApi.QuantityAsync(fixture, panadol)).Should().Be(11m, "4 opening + 2 boxes × 3 + 1 strip");
        var created = body["items"]![2]!["productId"]!.GetValue<Guid>();
        body["items"]![3]!["productId"]!.GetValue<Guid>().Should().Be(created, "same normalized name and base unit share one product");
        body["items"]![2]!["productName"]!.GetValue<string>().Should().Be("فيتامين سي");
        (await fixture.ScalarAsync<decimal>("SELECT cost_price FROM products WHERE id = @p0", created)).Should().Be(30m, "the lowest line sets the cost");
        (await fixture.ScalarAsync<decimal>("SELECT sell_price FROM product_units WHERE product_id = @p0 AND is_base", created)).Should().Be(40m);
        (await StockFlowApi.QuantityAsync(fixture, created)).Should().Be(5m);
        (await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM stock_movements WHERE invoice_id = @p0 AND reason = 1", invoiceId)).Should().Be(4, "one InvoiceReceipt per line");
        (await fixture.ScalarAsync<string>(
            "SELECT string_agg(balance_after::text, ',' ORDER BY balance_after) FROM stock_movements WHERE product_id = @p0 AND reason = 1",
            panadol)).Should().Be("10.000,11.000", "two lines of one product chain their balances");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM invoice_items WHERE invoice_id = @p0 AND confirmed_at IS NULL", invoiceId))
            .Should().Be(0);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM suppliers WHERE shop_id = @p0", session.ShopId)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_aliases WHERE shop_id = @p0", session.ShopId))
            .Should().Be(3, "the two vitamin lines normalize to one alias");
        (await fixture.ScalarAsync<int>(
            "SELECT times_confirmed FROM product_aliases WHERE shop_id = @p0 AND product_id = @p1", session.ShopId, created)).Should().Be(2);
    }

    [Fact]
    public async Task Confirm_SecondInvoiceFromTheSameSupplier_ReusesSupplierAndCountsTheAlias()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "شاش");
        var first = await StockFlowApi.ReviewedInvoiceAsync(fixture, session, "مورد", new ReviewLine(1, "شاش طبي", "1", "5.00", product, unit));
        var second = await StockFlowApi.ReviewedInvoiceAsync(fixture, session, "مورد", new ReviewLine(1, "شاش طبي", "2", "5.00", product, unit));

        (await StockFlowApi.ConfirmAsync(client, first, session.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        (await StockFlowApi.ConfirmAsync(client, second, session.AccessToken)).Status.Should().Be(HttpStatusCode.OK);

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM suppliers WHERE shop_id = @p0", session.ShopId)).Should().Be(1);
        (await fixture.ScalarAsync<int>("SELECT times_confirmed FROM product_aliases WHERE shop_id = @p0", session.ShopId)).Should().Be(2);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(3m);
    }

    [Fact]
    public async Task Confirm_Repeat_200SameInvoiceAndNoNewMovements()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "قطن");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(fixture, session, null, new ReviewLine(1, "قطن", "3", "2.00", product, unit));

        var first = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);
        var repeat = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);

        repeat.Status.Should().Be(HttpStatusCode.OK, repeat.Raw);
        repeat.Raw.Should().Be(first.Raw);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE invoice_id = @p0", invoiceId)).Should().Be(1);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(3m);
        first.Body!["supplierId"].Should().BeNull("no supplier name, no supplier");
    }

    [Fact]
    public async Task Confirm_TwoInParallel_ExactlyOneReceiptPerLineAndBoth200()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "مطهر");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(
            fixture,
            session,
            "مورد متوازي",
            new ReviewLine(1, "مطهر", "2", "9.00", product, unit),
            new ReviewLine(2, "مطهر كبير", "5", "9.00", product, unit),
            new ReviewLine(3, "منتج جديد", "1", "9.00", NewName: "منتج جديد", NewBaseUnit: "قطعة", NewSellPrice: "12.00"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken)));

        responses.Select(response => response.Status).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        (await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM stock_movements m JOIN invoice_items i ON i.id = m.invoice_item_id WHERE i.invoice_id = @p0 AND m.reason = 1",
            invoiceId)).Should().Be(3);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(7m);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE shop_id = @p0 AND name = 'منتج جديد'", session.ShopId)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM suppliers WHERE shop_id = @p0", session.ShopId)).Should().Be(1);
    }

    [Fact]
    public async Task Confirm_ProcessingInvoice_409NotConfirmable()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var invoiceId = await InvoiceApi.ScanAsync(client, session.AccessToken);

        var response = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Conflict, response.Raw);
        response.Code.Should().Be("Invoice.NotConfirmable");
    }

    [Fact]
    public async Task Confirm_LineWithoutProduct_400AndNothingChanges()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "لاصق");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(
            fixture, session, "مورد جديد", new ReviewLine(1, "لاصق", "1", "1.00", product, unit), new ReviewLine(2, "غير معروف", "1", "1.00"));

        var response = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Invoice.LineProductRequired");
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(0m);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM suppliers WHERE shop_id = @p0", session.ShopId)).Should().Be(0);
        (await fixture.ScalarAsync<short>("SELECT status FROM invoices WHERE id = @p0", invoiceId)).Should().Be(2);
    }

    [Fact]
    public async Task Confirm_OtherShopsOrUnknownInvoice_404InvoiceNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, owner.AccessToken, "ضماد");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(fixture, owner, null, new ReviewLine(1, "ضماد", "1", "1.00", product, unit));

        var foreign = await StockFlowApi.ConfirmAsync(client, invoiceId, stranger.AccessToken);
        var unknown = await StockFlowApi.ConfirmAsync(client, Guid.CreateVersion7(), owner.AccessToken);

        foreign.Status.Should().Be(HttpStatusCode.NotFound, "wrong tenant is 404 (rule 10)");
        foreign.Code.Should().Be("Invoice.NotFound");
        unknown.Code.Should().Be("Invoice.NotFound");
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(0m);
    }

    [Fact]
    public async Task Confirm_BumpsTheCatalogVersion_ProductListShowsTheNewQuantity()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "سرنجة");
        var before = await ProductApi.GetAsync(client, ProductApi.Products, session.AccessToken);
        before.Body!["items"]![0]!["quantity"]!.GetValue<string>().Should().Be("0.000");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(fixture, session, null, new ReviewLine(1, "سرنجة", "6", "1.00", product, unit));

        await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);
        var after = await ProductApi.GetAsync(client, ProductApi.Products, session.AccessToken);

        after.Body!["items"]![0]!["quantity"]!.GetValue<string>().Should().Be("6.000", "the cached page was invalidated after the commit");
    }

    [Fact]
    public async Task Confirm_RedisDown_StillConfirms()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "كمامة");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(fixture, session, "مورد", new ReviewLine(1, "كمامة", "2", "1.00", product, unit));

        var response = await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(2m);
    }
}
