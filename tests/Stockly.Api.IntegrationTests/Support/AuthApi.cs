using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Calls the auth endpoints (A1–A5) the way the mobile app does.</summary>
internal static class AuthApi
{
    public const string Password = "Str0ngPass";
    public const string Register = "/api/auth/register";
    public const string Login = "/api/auth/login";
    public const string Refresh = "/api/auth/refresh";
    public const string Logout = "/api/auth/logout";
    public const string Me = "/api/auth/me";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static object RegisterBody(
        string email,
        string shopName = "صيدلية الشفاء",
        string shopType = "Pharmacy",
        string password = Password,
        string? deviceId = "pixel-7",
        string? preferredLanguage = null) => new
        {
            fullName = "Mona Adel",
            email,
            password,
            phone = "01001234567",
            preferredLanguage,
            deviceId,
            shop = new { name = shopName, shopType, phone = (string?)null, governorate = "القاهرة", address = (string?)null },
        };

    public static async Task<Session> RegisterAsync(HttpClient client, string? email = null)
    {
        var address = email ?? NewEmail();
        var response = await PostAsync(client, Register, RegisterBody(address));
        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        return Session.From(address, response.Body!);
    }

    public static async Task<Session> LoginAsync(HttpClient client, string email, string? deviceId = null)
    {
        var response = await PostAsync(client, Login, new { email, password = Password, deviceId });
        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        return Session.From(email, response.Body!);
    }

    public static Task<ApiResponse> RefreshAsync(HttpClient client, string refreshToken) =>
        PostAsync(client, Refresh, new { refreshToken });

    public static Task<ApiResponse> LogoutAsync(HttpClient client, string accessToken, string refreshToken) =>
        PostAsync(client, Logout, new { refreshToken }, bearer: accessToken);

    public static Task<ApiResponse> MeAsync(HttpClient client, string? accessToken, string? language = null) =>
        SendAsync(client, new HttpRequestMessage(HttpMethod.Get, new Uri(Me, UriKind.Relative)), language, accessToken);

    public static Task<ApiResponse> PostAsync(HttpClient client, string path, object body, string? language = null, string? bearer = null) =>
        SendAsync(client, new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) }, language, bearer);

    /// <summary>A POST from the given client IP (see <see cref="TestClientIp"/>).</summary>
    public static Task<ApiResponse> PostFromAsync(HttpClient client, string clientIp, string path, object body, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add(TestClientIp.Header, clientIp);
        return SendAsync(client, request, language);
    }

    public static async Task<ApiResponse> SendAsync(HttpClient client, HttpRequestMessage request, string? language = null, string? bearer = null)
    {
        using (request)
        {
            if (language is not null)
            {
                request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
            }

            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            using var response = await client.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            return new ApiResponse(
                response.StatusCode,
                raw.Length == 0 ? null : JsonNode.Parse(raw),
                raw,
                response.Content.Headers.ContentType?.MediaType,
                response.Headers.WwwAuthenticate.ToString(),
                response.Headers.Location,
                response.Headers.TryGetValues("Retry-After", out var retryAfter) ? retryAfter.Single() : null);
        }
    }
}

internal sealed record ApiResponse(
    HttpStatusCode Status, JsonNode? Body, string Raw, string? ContentType, string WwwAuthenticate, Uri? Location, string? RetryAfter)
{
    public string? Code => Body?["code"]?.GetValue<string>();
}

internal sealed record Session(string Email, string AccessToken, string RefreshToken, Guid UserId, Guid ShopId, JsonNode Body)
{
    public static Session From(string email, JsonNode body) => new(
        email,
        body["accessToken"]!.GetValue<string>(),
        body["refreshToken"]!.GetValue<string>(),
        body["user"]!["id"]!.GetValue<Guid>(),
        body["shop"]!["id"]!.GetValue<Guid>(),
        body);
}
