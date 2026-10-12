using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Calls the products endpoints (P1–P11) the way the mobile app does.</summary>
internal static class ProductApi
{
    public const string Products = "/api/products";

    public static object CreateBody(
        string name = "بنادول اكسترا",
        string? barcode = null,
        string costPrice = "12.50",
        string baseUnitSellPrice = "15.00",
        string? lowStockThreshold = "5.000",
        string? openingQuantity = null,
        string? category = "مسكنات") => new
        {
            name,
            barcode,
            category,
            baseUnitName = "شريط",
            costPrice,
            baseUnitSellPrice,
            lowStockThreshold,
            openingQuantity,
        };

    public static async Task<Guid> CreateAsync(HttpClient client, string token, object? body = null)
    {
        var response = await PostAsync(client, Products, body ?? CreateBody(), token);
        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        return response.Body!["id"]!.GetValue<Guid>();
    }

    public static Task<ApiResponse> GetAsync(HttpClient client, string path, string? token, string? language = null) =>
        AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)), language, token);

    public static Task<ApiResponse> PostAsync(HttpClient client, string path, object body, string? token, string? language = null) =>
        Send(client, HttpMethod.Post, path, body, token, language);

    public static Task<ApiResponse> PutAsync(HttpClient client, string path, object body, string? token) =>
        Send(client, HttpMethod.Put, path, body, token);

    public static Task<ApiResponse> DeleteAsync(HttpClient client, string path, string? token) =>
        AuthApi.SendAsync(client, new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative)), bearer: token);

    /// <summary>A POST with a raw JSON body (e.g. a number where the contract wants a string).</summary>
    public static Task<ApiResponse> PostRawAsync(HttpClient client, string path, string json, string? token) =>
        AuthApi.SendAsync(
            client,
            new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
            bearer: token);

    public static string Product(Guid id) => $"{Products}/{id}";

    public static string Units(Guid id) => $"{Products}/{id}/units";

    public static string Unit(Guid id, Guid unitId) => $"{Products}/{id}/units/{unitId}";

    public static string Adjustments(Guid id) => $"{Products}/{id}/adjustments";

    public static string StockCounts(Guid id) => $"{Products}/{id}/stock-counts";

    public static string Movements(Guid id) => $"{Products}/{id}/movements";

    public static string Archive(Guid id) => $"{Products}/{id}/archive";

    private static Task<ApiResponse> Send(HttpClient client, HttpMethod method, string path, object body, string? token, string? language = null) =>
        AuthApi.SendAsync(client, new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) }, language, token);
}
