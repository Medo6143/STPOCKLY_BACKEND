using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Sales;

/// <summary>
/// Contract S1–S3 on the real host with PostgreSQL and Redis: prices, base-unit stock out, negative-stock warnings,
/// idempotency by <c>clientRequestId</c>, parallel sales, lock order against a parallel confirm, and the error codes.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class SalesEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateSale_DefaultAndOverriddenPrices_201WithLinesStockAndNegativeWarning()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, strip) = await StockFlowApi.ProductAsync(client, session.AccessToken, "بنادول", openingQuantity: "5.000");
        var box = await StockFlowApi.UnitAsync(client, session.AccessToken, product, "علبة", "3.000000", "40.00");
        var clientRequestId = Guid.CreateVersion7();

        var response = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(
            clientRequestId,
            StockFlowApi.Line(product, box, "2"),
            StockFlowApi.Line(product, strip, "1.5", "14.25")));

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        var sale = response.Body!;
        var saleId = sale["id"]!.GetValue<Guid>();
        response.Location!.ToString().Should().Be($"/api/sales/{saleId}");
        sale["clientRequestId"]!.GetValue<Guid>().Should().Be(clientRequestId);
        sale["totalAmount"]!.GetValue<string>().Should().Be("101.38", "80.00 + round(1.5 × 14.25 = 21.375, 2) away from zero");
        sale["priceOverridden"]!.GetValue<bool>().Should().BeTrue();
        sale["createdByUserId"]!.GetValue<Guid>().Should().Be(session.UserId);
        var items = sale["items"]!.AsArray();
        items[0]!["listUnitPrice"]!.GetValue<string>().Should().Be("40.00");
        items[0]!["unitPrice"]!.GetValue<string>().Should().Be("40.00");
        items[0]!["priceOverridden"]!.GetValue<bool>().Should().BeFalse();
        items[0]!["unitName"]!.GetValue<string>().Should().Be("علبة");
        items[0]!["balanceAfter"]!.GetValue<string>().Should().Be("-1.000", "5 − 2 boxes × 3");
        items[1]!["listUnitPrice"]!.GetValue<string>().Should().Be("15.00");
        items[1]!["unitPrice"]!.GetValue<string>().Should().Be("14.25");
        items[1]!["priceOverridden"]!.GetValue<bool>().Should().BeTrue();
        items[1]!["lineTotal"]!.GetValue<string>().Should().Be("21.38");
        items[1]!["quantity"]!.GetValue<string>().Should().Be("1.500");
        items[1]!["balanceAfter"]!.GetValue<string>().Should().Be("-2.500");
        sale["warnings"]!.AsArray().Should().ContainSingle();
        sale["warnings"]![0]!["code"]!.GetValue<string>().Should().Be("Stock.NegativeBalance");
        sale["warnings"]![0]!["productId"]!.GetValue<Guid>().Should().Be(product);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(-2.5m, "negative stock never blocks a sale");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE sale_id = @p0 AND reason = 2", saleId)).Should().Be(2);

        var read = await ProductApi.GetAsync(client, StockFlowApi.Sale(saleId), session.AccessToken);
        read.Status.Should().Be(HttpStatusCode.OK);
        read.Raw.Should().Be(response.Raw, "S3 returns the same projection S1 returned");
    }

    [Fact]
    public async Task CreateSale_RepeatClientRequestId_200ExistingSaleAndNoNewMovements()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "قطن", openingQuantity: "10.000");
        var body = StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "1"));

        var first = await StockFlowApi.SellAsync(client, session.AccessToken, body);
        var repeat = await StockFlowApi.SellAsync(client, session.AccessToken, body);

        first.Status.Should().Be(HttpStatusCode.Created);
        repeat.Status.Should().Be(HttpStatusCode.OK, repeat.Raw);
        repeat.Raw.Should().Be(first.Raw);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(9m);
    }

    [Fact]
    public async Task CreateSale_SameClientRequestIdInParallel_OneSale()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "شاش", openingQuantity: "10.000");
        var body = StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "1"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => StockFlowApi.SellAsync(client, session.AccessToken, body)));

        responses.Select(response => response.Status).Should().BeSubsetOf([HttpStatusCode.Created, HttpStatusCode.OK]);
        responses.Count(response => response.Status == HttpStatusCode.Created).Should().Be(1);
        responses.Select(response => response.Body!["id"]!.GetValue<Guid>()).Distinct().Should().ContainSingle();
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @p0", session.ShopId)).Should().Be(1);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(9m);
    }

    [Fact]
    public async Task CreateSale_TwoSalesOfOneProductInParallel_ChainedBalances()
    {
        const int Sales = 6;
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "مطهر", openingQuantity: "10.000");

        var responses = await Task.WhenAll(Enumerable.Range(0, Sales).Select(_ =>
            StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "1")))));

        responses.Select(response => response.Status).Should().AllBeEquivalentTo(HttpStatusCode.Created);
        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(10m - Sales);
        responses.Select(response => response.Body!["items"]![0]!["balanceAfter"]!.GetValue<string>()).Order()
            .Should().Equal(Enumerable.Range(10 - Sales, Sales).Select(n => $"{n}.000").Order(), "each sale starts from the previous balance");
    }

    [Fact]
    public async Task ConfirmAndSaleOnOverlappingProducts_InParallel_NoDeadlockAndBothApplied()
    {
        const int Rounds = 5;
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (first, firstUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "أ", openingQuantity: "100.000");
        var (second, secondUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "ب", openingQuantity: "100.000");

        for (var round = 0; round < Rounds; round++)
        {
            // The invoice lists the products in one order and the sale in the other; both lock by id.
            var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(
                fixture,
                session,
                null,
                new ReviewLine(1, "ب", "1", "1.00", second, secondUnit),
                new ReviewLine(2, "أ", "1", "1.00", first, firstUnit));
            var confirm = StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken);
            var sale = StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(
                Guid.CreateVersion7(), StockFlowApi.Line(first, firstUnit, "2"), StockFlowApi.Line(second, secondUnit, "2")));

            var results = await Task.WhenAll(confirm, sale);

            results[0].Status.Should().Be(HttpStatusCode.OK, results[0].Raw);
            results[1].Status.Should().Be(HttpStatusCode.Created, results[1].Raw);
        }

        (await StockFlowApi.QuantityAsync(fixture, first)).Should().Be(100m - Rounds);
        (await StockFlowApi.QuantityAsync(fixture, second)).Should().Be(100m - Rounds);
    }

    [Fact]
    public async Task CreateSale_ArchivedProduct_400SaleArchivedProductAndNothingSaved()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (live, liveUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "حي", openingQuantity: "3.000");
        var (archived, archivedUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "مؤرشف");
        (await ProductApi.PostAsync(client, ProductApi.Archive(archived), new { }, session.AccessToken)).Status.Should().Be(HttpStatusCode.NoContent);

        var response = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(
            Guid.CreateVersion7(), StockFlowApi.Line(live, liveUnit, "1"), StockFlowApi.Line(archived, archivedUnit, "1")));

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Sale.ArchivedProduct");
        (await StockFlowApi.QuantityAsync(fixture, live)).Should().Be(3m, "the whole sale rolled back");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @p0", session.ShopId)).Should().Be(0);
    }

    [Fact]
    public async Task CreateSale_UnitErrors_400UnitNotOfProductOr404()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var (product, _) = await StockFlowApi.ProductAsync(client, session.AccessToken, "أول");
        var (_, otherUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "ثاني");
        var (foreign, foreignUnit) = await StockFlowApi.ProductAsync(client, stranger.AccessToken, "غريب");

        var wrongUnit = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, otherUnit, "1")));
        var unknownUnit = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, Guid.CreateVersion7(), "1")));
        var otherShop = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(foreign, foreignUnit, "1")));

        wrongUnit.Status.Should().Be(HttpStatusCode.BadRequest, wrongUnit.Raw);
        wrongUnit.Code.Should().Be("Sale.UnitNotOfProduct");
        unknownUnit.Status.Should().Be(HttpStatusCode.NotFound);
        unknownUnit.Code.Should().Be("ProductUnit.NotFound");
        otherShop.Status.Should().Be(HttpStatusCode.NotFound, "another shop's product is invisible (rule 10)");
        otherShop.Code.Should().Be("Product.NotFound");
    }

    [Fact]
    public async Task CreateSale_ShapeAndPrecision_400()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "دقيق");
        var tooMany = Enumerable.Range(0, 101).Select(_ => StockFlowApi.Line(product, unit, "1")).ToArray();

        var overCap = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), tooMany));
        var noId = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.Empty, StockFlowApi.Line(product, unit, "1")));
        var zero = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "0")));
        var precise = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "1.0005")));
        var price = await StockFlowApi.SellAsync(client, session.AccessToken, StockFlowApi.SaleBody(Guid.CreateVersion7(), StockFlowApi.Line(product, unit, "1", "-1.00")));
        var number = await ProductApi.PostRawAsync(
            client, StockFlowApi.Sales, $$"""{"clientRequestId":"{{Guid.CreateVersion7()}}","items":[{"productId":"{{product}}","productUnitId":"{{unit}}","quantity":1}]}""", session.AccessToken);

        overCap.Code.Should().Be("Validation.Failed");
        overCap.Body!["errors"]!["items"].Should().NotBeNull();
        noId.Code.Should().Be("Validation.Failed");
        zero.Code.Should().Be("Validation.Failed");
        precise.Code.Should().Be("Precision.TooManyDecimals");
        price.Code.Should().Be("Precision.Negative");
        number.Code.Should().Be("Validation.Failed", "decimal fields are strings");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @p0", session.ShopId)).Should().Be(0);
    }

    [Fact]
    public async Task ListSales_PagedNewestFirstWithRangeAndOnlyThisShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var other = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "سكر");
        var (otherProduct, otherUnit) = await StockFlowApi.ProductAsync(client, other.AccessToken, "سكر");
        var sales = new List<(Guid Id, string CreatedAt)>();
        for (var index = 0; index < 3; index++)
        {
            var sale = await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(product, unit, "1"));
            sales.Add((sale["id"]!.GetValue<Guid>(), sale["createdAt"]!.GetValue<string>()));
            await Task.Delay(5);
        }

        await StockFlowApi.SellOkAsync(client, other.AccessToken, StockFlowApi.Line(otherProduct, otherUnit, "1"));

        var page = await ProductApi.GetAsync(client, $"{StockFlowApi.Sales}?page=1&pageSize=2", session.AccessToken);
        var range = await ProductApi.GetAsync(
            client, $"{StockFlowApi.Sales}?from={Uri.EscapeDataString(sales[1].CreatedAt)}&to={Uri.EscapeDataString(sales[2].CreatedAt)}", session.AccessToken);
        var reversed = await ProductApi.GetAsync(
            client, $"{StockFlowApi.Sales}?from=2026-10-15T11:00:00Z&to=2026-10-15T10:00:00Z", session.AccessToken);
        var tooBig = await ProductApi.GetAsync(client, $"{StockFlowApi.Sales}?pageSize=101", session.AccessToken);

        page.Status.Should().Be(HttpStatusCode.OK, page.Raw);
        page.Body!["totalCount"]!.GetValue<int>().Should().Be(3);
        page.Body["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Should().Equal(sales[2].Id, sales[1].Id);
        page.Body["items"]![0]!["itemCount"]!.GetValue<int>().Should().Be(1);
        page.Body["items"]![0]!["totalAmount"]!.GetValue<string>().Should().Be("15.00");
        range.Status.Should().Be(HttpStatusCode.OK, range.Raw);
        range.Body!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Should().Equal([sales[1].Id], "from is inclusive, to exclusive");
        reversed.Code.Should().Be("Validation.Failed");
        tooBig.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task GetSale_OtherShopsOrUnknown_404SaleNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, owner.AccessToken, "ملح");
        var saleId = (await StockFlowApi.SellOkAsync(client, owner.AccessToken, StockFlowApi.Line(product, unit, "1")))["id"]!.GetValue<Guid>();

        var foreign = await ProductApi.GetAsync(client, StockFlowApi.Sale(saleId), stranger.AccessToken);
        var unknown = await ProductApi.GetAsync(client, StockFlowApi.Sale(Guid.CreateVersion7()), owner.AccessToken);

        foreign.Status.Should().Be(HttpStatusCode.NotFound);
        foreign.Code.Should().Be("Sale.NotFound");
        unknown.Code.Should().Be("Sale.NotFound");
    }

    [Fact]
    public async Task CreateSale_RedisDown_StillRecorded()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "أرز", openingQuantity: "2.000");

        await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(product, unit, "1"));

        (await StockFlowApi.QuantityAsync(fixture, product)).Should().Be(1m);
    }
}
