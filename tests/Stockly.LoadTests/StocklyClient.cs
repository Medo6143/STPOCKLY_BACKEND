using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Stockly.LoadTests;

/// <summary>A product the sales scenario sells: its id and its base unit.</summary>
internal sealed record SellableProduct(Guid ProductId, Guid BaseUnitId);

/// <summary>An invoice of the scan scenario and the token of its shop.</summary>
internal sealed record ScannedInvoice(Guid InvoiceId, string Token);

/// <summary>The few contract calls the load test needs (A1, P1, P3, S1, I1, I3), as the mobile app sends them.</summary>
internal sealed class StocklyClient(HttpClient http)
{
    public const string Password = "LoadTest1234";

    /// <summary>JPEG magic bytes: the scan's type check reads only these.</summary>
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    /// <summary>A1: a new owner and shop; returns the access token.</summary>
    public async Task<string> RegisterShopAsync(int number)
    {
        var body = new
        {
            fullName = "Load Test Owner",
            email = $"loadtest-{Guid.NewGuid():N}@example.com",
            password = Password,
            phone = "01000000000",
            preferredLanguage = "en",
            deviceId = "load-test",
            shop = new
            {
                name = string.Create(CultureInfo.InvariantCulture, $"صيدلية اختبار الحمل {number}"),
                shopType = "Pharmacy",
                phone = (string?)null,
                governorate = "القاهرة",
                address = (string?)null,
            },
        };
        using var response = await http.PostAsJsonAsync(new Uri("/api/auth/register", UriKind.Relative), body);
        await EnsureAsync(response, HttpStatusCode.Created, "register");
        var json = await ReadAsync(response);
        return json["accessToken"]!.GetValue<string>();
    }

    /// <summary>P3: a product with opening stock; returns it with its base unit.</summary>
    public async Task<SellableProduct> CreateProductAsync(string token, string name)
    {
        var body = new
        {
            name,
            barcode = (string?)null,
            category = "Load test",
            baseUnitName = "شريط",
            costPrice = "10.00",
            baseUnitSellPrice = "12.50",
            lowStockThreshold = "5.000",
            openingQuantity = "100000.000",
        };
        using var request = Authorized(HttpMethod.Post, "/api/products", token);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        await EnsureAsync(response, HttpStatusCode.Created, "create product");
        var json = await ReadAsync(response);
        var baseUnit = json["units"]!.AsArray().Single(unit => unit!["isBase"]!.GetValue<bool>())!;
        return new SellableProduct(json["id"]!.GetValue<Guid>(), baseUnit["id"]!.GetValue<Guid>());
    }

    /// <summary>P1: the first page of the product list.</summary>
    public async Task<bool> ListProductsAsync(string token)
    {
        using var request = Authorized(HttpMethod.Get, "/api/products?page=1&pageSize=20", token);
        using var response = await http.SendAsync(request);
        return ScenarioRunner.IsSuccess(response.StatusCode);
    }

    /// <summary>S1: a sale of one or two lines with a fresh <c>clientRequestId</c>.</summary>
    public async Task<bool> SellAsync(string token, IReadOnlyList<SellableProduct> products)
    {
        var first = products[RandomNumberGenerator.GetInt32(products.Count)];
        var second = products[RandomNumberGenerator.GetInt32(products.Count)];
        var lines = first == second
            ? new[] { Line(first) }
            : new[] { Line(first), Line(second) };
        using var request = Authorized(HttpMethod.Post, "/api/sales", token);
        request.Content = JsonContent.Create(new { clientRequestId = Guid.CreateVersion7(), note = (string?)null, items = lines });
        using var response = await http.SendAsync(request);
        return ScenarioRunner.IsSuccess(response.StatusCode);
    }

    /// <summary>I1: a scan of a unique image (so the AI cache never answers); returns the invoice id, or null on failure.</summary>
    public async Task<Guid?> ScanAsync(string token, int imageKilobytes)
    {
        var image = new byte[imageKilobytes * 1024];
        RandomNumberGenerator.Fill(image);
        JpegHeader.CopyTo(image, 0);
        using var form = new MultipartFormDataContent { { new StringContent(Guid.CreateVersion7().ToString()), "clientRequestId" } };
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "image", "invoice.jpg");
        using var request = Authorized(HttpMethod.Post, "/api/invoices/scan", token);
        request.Content = form;
        using var response = await http.SendAsync(request);
        if (!ScenarioRunner.IsSuccess(response.StatusCode))
        {
            return null;
        }

        return (await ReadAsync(response))["id"]!.GetValue<Guid>();
    }

    /// <summary>I3: the invoice, as the app polls it while it is processing.</summary>
    public async Task<bool> GetInvoiceAsync(ScannedInvoice invoice)
    {
        using var request = Authorized(HttpMethod.Get, $"/api/invoices/{invoice.InvoiceId}", invoice.Token);
        using var response = await http.SendAsync(request);
        return ScenarioRunner.IsSuccess(response.StatusCode);
    }

    /// <summary>How many of the invoices are still <c>Processing</c> (the status cache answers those).</summary>
    public async Task<int> CountProcessingAsync(IReadOnlyList<ScannedInvoice> invoices)
    {
        var processing = 0;
        foreach (var invoice in invoices)
        {
            using var request = Authorized(HttpMethod.Get, $"/api/invoices/{invoice.InvoiceId}", invoice.Token);
            using var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode && (await ReadAsync(response))["status"]!.GetValue<string>() == "Processing")
            {
                processing++;
            }
        }

        return processing;
    }

    private static object Line(SellableProduct product) =>
        new { productId = product.ProductId, productUnitId = product.BaseUnitId, quantity = "1.000", unitPrice = (string?)null };

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException("Empty response body.");

    private static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected, string step)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Set-up step '{step}' returned {(int)response.StatusCode}: {body}");
        }
    }
}
