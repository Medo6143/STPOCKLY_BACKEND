using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Sales;

/// <summary>Contract P12 (barcode lookup) and D1/D2 (dashboard) on the real host with PostgreSQL and Redis.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class LookupAndDashboardEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Lookup_ProductAndUnitBarcodes_200WithTheMatchingUnit()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var productBarcode = $"622{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";
        var unitBarcode = $"{productBarcode}9";
        var (product, strip) = await StockFlowApi.ProductAsync(client, session.AccessToken, "بنادول", openingQuantity: "4.000", barcode: productBarcode);
        var box = await StockFlowApi.UnitAsync(client, session.AccessToken, product, "علبة", "3.000000", "40.00", unitBarcode);

        var byProduct = await ProductApi.GetAsync(client, StockFlowApi.Lookup(productBarcode), session.AccessToken);
        var byUnit = await ProductApi.GetAsync(client, StockFlowApi.Lookup(unitBarcode), session.AccessToken);

        byProduct.Status.Should().Be(HttpStatusCode.OK, byProduct.Raw);
        byProduct.Body!["product"]!["id"]!.GetValue<Guid>().Should().Be(product);
        byProduct.Body["product"]!["quantity"]!.GetValue<string>().Should().Be("4.000");
        byProduct.Body["unit"]!["id"]!.GetValue<Guid>().Should().Be(strip, "a product barcode names the base unit");
        byProduct.Body["unit"]!["isBase"]!.GetValue<bool>().Should().BeTrue();
        byUnit.Body!["product"]!["id"]!.GetValue<Guid>().Should().Be(product);
        byUnit.Body["unit"]!["id"]!.GetValue<Guid>().Should().Be(box);
        byUnit.Body["unit"]!["factor"]!.GetValue<string>().Should().Be("3.000000");
        byUnit.Body["unit"]!["barcode"]!.GetValue<string>().Should().Be(unitBarcode);
    }

    [Fact]
    public async Task Lookup_ArchivedOtherShopOrUnknown_404AndMissingBarcode_400()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var archivedBarcode = $"A{Guid.NewGuid():N}"[..20];
        var foreignBarcode = $"F{Guid.NewGuid():N}"[..20];
        var (archived, _) = await StockFlowApi.ProductAsync(client, session.AccessToken, "قديم", barcode: archivedBarcode);
        await ProductApi.PostAsync(client, ProductApi.Archive(archived), new { }, session.AccessToken);
        await StockFlowApi.ProductAsync(client, stranger.AccessToken, "غريب", barcode: foreignBarcode);

        var archivedLookup = await ProductApi.GetAsync(client, StockFlowApi.Lookup(archivedBarcode), session.AccessToken);
        var foreign = await ProductApi.GetAsync(client, StockFlowApi.Lookup(foreignBarcode), session.AccessToken);
        var unknown = await ProductApi.GetAsync(client, StockFlowApi.Lookup("0000000000000"), session.AccessToken);
        var missing = await ProductApi.GetAsync(client, $"{ProductApi.Products}/lookup", session.AccessToken);

        archivedLookup.Status.Should().Be(HttpStatusCode.NotFound);
        archivedLookup.Code.Should().Be("Product.NotFound");
        foreign.Code.Should().Be("Product.NotFound");
        unknown.Code.Should().Be("Product.NotFound");
        missing.Status.Should().Be(HttpStatusCode.BadRequest);
        missing.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task Summary_CountsTheShopsDay()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (stocked, stockedUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "متوفر", openingQuantity: "10.000");
        var (negative, negativeUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "سالب", openingQuantity: "1.000");
        await StockFlowApi.ProductAsync(client, session.AccessToken, "صفر");
        var (archived, _) = await StockFlowApi.ProductAsync(client, session.AccessToken, "مؤرشف");
        await ProductApi.PostAsync(client, ProductApi.Archive(archived), new { }, session.AccessToken);
        await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(stocked, stockedUnit, "1"));
        await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(negative, negativeUnit, "3", "10.50"));
        var expiring = await StockFlowApi.ReviewedInvoiceAsync(
            fixture, session, null, new ReviewLine(1, "متوفر", "1", "1.00", stocked, stockedUnit, Expiry: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))));
        (await StockFlowApi.ConfirmAsync(client, expiring, session.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        await StockFlowApi.ReviewedInvoiceAsync(fixture, session, null, new ReviewLine(1, "متوفر", "1", "1.00", stocked, stockedUnit));

        var response = await ProductApi.GetAsync(client, StockFlowApi.Summary, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        var summary = response.Body!;
        summary["productCount"]!.GetValue<int>().Should().Be(3, "archived products are not counted");
        summary["lowStockCount"]!.GetValue<int>().Should().Be(2, "quantity <= threshold 0: the negative and the empty product");
        summary["negativeStockCount"]!.GetValue<int>().Should().Be(1);
        summary["expiringSoonCount"]!.GetValue<int>().Should().Be(1);
        summary["todaySales"]!["count"]!.GetValue<int>().Should().Be(2);
        summary["todaySales"]!["total"]!.GetValue<string>().Should().Be("46.50", "15.00 + 3 × 10.50");
        summary["todaySales"]!["overriddenSalesCount"]!.GetValue<int>().Should().Be(1);
        summary["invoicesAwaitingReview"]!.GetValue<int>().Should().Be(1);
        summary["generatedAt"].Should().NotBeNull();
    }

    [Fact]
    public async Task Summary_CachedUntilACatalogWriteBumpsTheVersion()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var (product, unit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "خبز", openingQuantity: "5.000");

        var first = await ProductApi.GetAsync(client, StockFlowApi.Summary, session.AccessToken);
        await fixture.ExecuteAsync("UPDATE products SET low_stock_threshold = 100 WHERE id = @p0", product);
        var cached = await ProductApi.GetAsync(client, StockFlowApi.Summary, session.AccessToken);
        await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(product, unit, "1"));
        var fresh = await ProductApi.GetAsync(client, StockFlowApi.Summary, session.AccessToken);

        cached.Raw.Should().Be(first.Raw, "a write that bumps nothing is not seen for up to 2 minutes");
        fresh.Body!["lowStockCount"]!.GetValue<int>().Should().Be(1, "the sale bumped the catalog version after its commit");
        fresh.Body["todaySales"]!["count"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task Summary_RedisDown_200FromPostgres()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        await StockFlowApi.ProductAsync(client, session.AccessToken, "ماء");

        var response = await ProductApi.GetAsync(client, StockFlowApi.Summary, session.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["productCount"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task ExpiringSoon_WindowStockFilterOrderAndPaging()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (stocked, strip) = await StockFlowApi.ProductAsync(client, session.AccessToken, "متوفر");
        var box = await StockFlowApi.UnitAsync(client, session.AccessToken, stocked, "علبة", "3.000000", "40.00");
        var (sold, soldUnit) = await StockFlowApi.ProductAsync(client, session.AccessToken, "نفد");
        var invoiceId = await StockFlowApi.ReviewedInvoiceAsync(
            fixture,
            session,
            null,
            new ReviewLine(1, "بعيد", "1", "1.00", stocked, strip, Expiry: today.AddDays(30), Batch: "B2"),
            new ReviewLine(2, "قريب", "2", "1.00", stocked, box, Expiry: today.AddDays(5), Batch: "B1"),
            new ReviewLine(3, "خارج النافذة", "1", "1.00", stocked, strip, Expiry: today.AddDays(90)),
            new ReviewLine(4, "بلا تاريخ", "1", "1.00", stocked, strip),
            new ReviewLine(5, "نفد", "1", "1.00", sold, soldUnit, Expiry: today.AddDays(3)),
            new ReviewLine(6, "جديد", "4", "1.00", NewName: "جديد", NewBaseUnit: "قطعة", NewSellPrice: "2.00", Expiry: today.AddDays(20)));
        (await StockFlowApi.ConfirmAsync(client, invoiceId, session.AccessToken)).Status.Should().Be(HttpStatusCode.OK);
        await StockFlowApi.SellOkAsync(client, session.AccessToken, StockFlowApi.Line(sold, soldUnit, "1"));
        await StockFlowApi.ReviewedInvoiceAsync(fixture, session, null, new ReviewLine(1, "غير مؤكد", "1", "1.00", stocked, strip, Expiry: today.AddDays(1)));

        var all = await ProductApi.GetAsync(client, $"{StockFlowApi.ExpiringSoon}?days=60", session.AccessToken);
        var paged = await ProductApi.GetAsync(client, $"{StockFlowApi.ExpiringSoon}?days=60&page=2&pageSize=2", session.AccessToken);
        var wide = await ProductApi.GetAsync(client, $"{StockFlowApi.ExpiringSoon}?days=365", session.AccessToken);
        var invalid = await ProductApi.GetAsync(client, $"{StockFlowApi.ExpiringSoon}?days=0", session.AccessToken);

        all.Status.Should().Be(HttpStatusCode.OK, all.Raw);
        all.Body!["totalCount"]!.GetValue<int>().Should().Be(3, "unconfirmed, undated, out-of-window and out-of-stock lines are left out");
        var items = all.Body["items"]!.AsArray();
        items.Select(item => item!["batchNo"]?.GetValue<string>()).Should().Equal("B1", null, "B2");
        items[0]!["lineQuantity"]!.GetValue<string>().Should().Be("6.000", "2 boxes in base units");
        items[0]!["currentQuantity"]!.GetValue<string>().Should().Be("9.000", "1 + 6 + 1 + 1 received");
        items[0]!["invoiceId"]!.GetValue<Guid>().Should().Be(invoiceId);
        items[0]!["expiryDate"]!.GetValue<string>().Should().Be(today.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        items[1]!["productName"]!.GetValue<string>().Should().Be("جديد", "a new product's line names the created product");
        items[1]!["lineQuantity"]!.GetValue<string>().Should().Be("4.000");
        paged.Body!["items"]!.AsArray().Should().ContainSingle();
        wide.Body!["totalCount"]!.GetValue<int>().Should().Be(4);
        invalid.Code.Should().Be("Validation.Failed");
    }
}
