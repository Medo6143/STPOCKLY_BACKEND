using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Calls the invoice endpoints (I1–I4) the way the mobile app does.</summary>
internal static class InvoiceApi
{
    public const string Invoices = "/api/invoices";

    /// <summary>The smallest bytes that pass the JPEG type check.</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    public static string Invoice(Guid id) => $"{Invoices}/{id}";

    public static string Items(Guid id) => $"{Invoices}/{id}/items";

    /// <summary>A new scan with a fresh <c>clientRequestId</c>; returns the invoice id.</summary>
    public static async Task<Guid> ScanAsync(HttpClient client, string token, byte[]? image = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(Guid.CreateVersion7().ToString()), "clientRequestId" } };
        var file = new ByteArrayContent(image ?? Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "image", "invoice.jpg");
        var response = await AuthApi.SendAsync(
            client, new HttpRequestMessage(HttpMethod.Post, new Uri($"{Invoices}/scan", UriKind.Relative)) { Content = form }, bearer: token);
        response.Status.Should().Be(HttpStatusCode.Accepted, response.Raw);
        return response.Body!["id"]!.GetValue<Guid>();
    }

    public static Task<ApiResponse> GetAsync(HttpClient client, string path, string? token) =>
        ProductApi.GetAsync(client, path, token);

    public static Task<ApiResponse> PutItemsAsync(HttpClient client, Guid id, object body, string token) =>
        ProductApi.PutAsync(client, Items(id), body, token);

    public static Task<ApiResponse> PutItemsRawAsync(HttpClient client, Guid id, string json, string token) =>
        AuthApi.SendAsync(
            client,
            new HttpRequestMessage(HttpMethod.Put, new Uri(Items(id), UriKind.Relative)) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
            bearer: token);

    /// <summary>Polls I3 every 200 ms, as the app does every 2 s, until the invoice leaves <c>Processing</c>.</summary>
    public static async Task<ApiResponse> PollUntilProcessedAsync(HttpClient client, Guid id, string token, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        ApiResponse response;
        do
        {
            response = await GetAsync(client, Invoice(id), token);
            response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
            if (response.Body!["status"]!.GetValue<string>() != "Processing")
            {
                return response;
            }

            await Task.Delay(200);
        }
        while (watch.Elapsed < timeout);

        return response;
    }
}
