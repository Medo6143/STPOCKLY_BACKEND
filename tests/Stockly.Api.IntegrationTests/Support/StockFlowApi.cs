using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Sprint 4 calls: confirm (I5), sales (S1–S3), barcode lookup (P12), dashboard (D1, D2), and their arrangements.</summary>
internal static class StockFlowApi
{
    public const string Sales = "/api/sales";
    public const string Summary = "/api/dashboard/summary";
    public const string ExpiringSoon = "/api/dashboard/expiring-soon";

    public static string Confirm(Guid invoiceId) => $"{InvoiceApi.Invoices}/{invoiceId}/confirm";

    public static string Sale(Guid saleId) => $"{Sales}/{saleId}";

    public static string Lookup(string barcode) => $"{ProductApi.Products}/lookup?barcode={Uri.EscapeDataString(barcode)}";

    public static Task<ApiResponse> ConfirmAsync(HttpClient client, Guid invoiceId, string token) =>
        AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Post, new Uri(Confirm(invoiceId), UriKind.Relative)), bearer: token);

    /// <summary>A product (Owner session) with opening stock and its base unit id.</summary>
    public static async Task<(Guid ProductId, Guid BaseUnitId)> ProductAsync(
        HttpClient client, string token, string name, string? openingQuantity = null, string? barcode = null, string sellPrice = "15.00")
    {
        var productId = await ProductApi.CreateAsync(
            client,
            token,
            ProductApi.CreateBody(name: name, barcode: barcode, openingQuantity: openingQuantity, baseUnitSellPrice: sellPrice, lowStockThreshold: "0.000"));
        var detail = await ProductApi.GetAsync(client, ProductApi.Product(productId), token);
        return (productId, detail.Body!["units"]![0]!["id"]!.GetValue<Guid>());
    }

    public static async Task<Guid> UnitAsync(HttpClient client, string token, Guid productId, string name, string factor, string sellPrice, string? barcode = null)
    {
        var response = await ProductApi.PostAsync(client, ProductApi.Units(productId), new { name, factor, sellPrice, barcode }, token);
        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        return response.Body!["id"]!.GetValue<Guid>();
    }

    public static object SaleBody(Guid clientRequestId, params object[] items) => new { clientRequestId, note = (string?)null, items };

    public static object Line(Guid productId, Guid unitId, string quantity, string? unitPrice = null) =>
        new { productId, productUnitId = unitId, quantity, unitPrice };

    public static Task<ApiResponse> SellAsync(HttpClient client, string token, object body) =>
        ProductApi.PostAsync(client, Sales, body, token);

    public static async Task<JsonNode> SellOkAsync(HttpClient client, string token, params object[] items)
    {
        var response = await SellAsync(client, token, SaleBody(Guid.CreateVersion7(), items));
        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        return response.Body!;
    }

    /// <summary>What the job and the review edit would have written: a NeedsReview invoice with these lines (raw SQL).</summary>
    public static async Task<Guid> ReviewedInvoiceAsync(
        ApiFixture fixture, Session session, string? supplierName, params ReviewLine[] lines)
    {
        var invoiceId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO invoices (id, shop_id, client_request_id, created_by_user_id, status, image_object_key, image_sha256, " +
            "supplier_name_raw, computed_total, processed_at, created_at) " +
            "VALUES (@p0, @p1, @p2, @p3, 2, 'invoices/test.jpg', repeat('a', 64), @p4, 0, now(), now())",
            invoiceId,
            session.ShopId,
            Guid.CreateVersion7(),
            session.UserId,
            (object?)supplierName ?? DBNull.Value);
        foreach (var line in lines)
        {
            var quantity = decimal.Parse(line.Quantity, CultureInfo.InvariantCulture);
            var unitPrice = decimal.Parse(line.UnitPrice, CultureInfo.InvariantCulture);
            await fixture.ExecuteAsync(
                "INSERT INTO invoice_items (id, shop_id, invoice_id, line_no, raw_name, quantity, unit_price, line_total, product_id, " +
                "product_unit_id, new_product_name, new_product_base_unit_name, new_product_sell_price, expiry_date, batch_no) " +
                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, round(@p5 * @p6, 2), @p7, @p8, @p9, @p10, @p11, @p12, @p13)",
                Guid.CreateVersion7(),
                session.ShopId,
                invoiceId,
                line.LineNo,
                line.RawName,
                quantity,
                unitPrice,
                (object?)line.ProductId ?? DBNull.Value,
                (object?)line.UnitId ?? DBNull.Value,
                (object?)line.NewName ?? DBNull.Value,
                (object?)line.NewBaseUnit ?? DBNull.Value,
                line.NewSellPrice is { } sell ? decimal.Parse(sell, CultureInfo.InvariantCulture) : DBNull.Value,
                (object?)line.Expiry ?? DBNull.Value,
                (object?)line.Batch ?? DBNull.Value);
        }

        return invoiceId;
    }

    public static async Task<decimal> QuantityAsync(ApiFixture fixture, Guid productId) =>
        await fixture.ScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @p0", productId);
}

/// <summary>One invoice line as stored: an existing product with its unit, or a new product.</summary>
internal sealed record ReviewLine(
    int LineNo,
    string RawName,
    string Quantity,
    string UnitPrice,
    Guid? ProductId = null,
    Guid? UnitId = null,
    string? NewName = null,
    string? NewBaseUnit = null,
    string? NewSellPrice = null,
    DateOnly? Expiry = null,
    string? Batch = null);
