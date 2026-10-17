using System.Net;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Calls the admin endpoints (contract §11) the way <c>Stockly.Admin</c> does, as the seeded test admin.</summary>
internal static class AdminApi
{
    /// <summary>Test-only admin, seeded at start-up by every factory on the fixture's database. Never a real account.</summary>
    public const string Email = "platform-admin@example.com";
    public const string Password = "Adm1nTestPass";
    public const string FullName = "Test Platform Admin";

    public const string Login = "/api/admin/auth/login";
    public const string Refresh = "/api/admin/auth/refresh";
    public const string Logout = "/api/admin/auth/logout";
    public const string Shops = "/api/admin/shops";
    public const string Plans = "/api/admin/plans";
    public const string AuditLogs = "/api/admin/audit-logs";
    public const string FailedInvoices = "/api/admin/invoices/failed";
    public const string HangfireSession = "/api/admin/hangfire/session";

    public static string Shop(Guid id) => $"{Shops}/{id}";

    public static string Invoice(Guid id) => $"/api/admin/invoices/{id}";

    public static async Task<AdminSession> LoginAsync(HttpClient client)
    {
        var response = await AuthApi.PostAsync(client, Login, new { email = Email, password = Password });
        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        return new AdminSession(
            response.Body!["accessToken"]!.GetValue<string>(),
            response.Body["refreshToken"]!.GetValue<string>(),
            response.Body["user"]!["id"]!.GetValue<Guid>());
    }

    public static Task<ApiResponse> GetAsync(HttpClient client, string path, string? token, string? language = null) =>
        ProductApi.GetAsync(client, path, token, language);

    public static Task<ApiResponse> PostAsync(HttpClient client, string path, object body, string? token, string? language = null) =>
        ProductApi.PostAsync(client, path, body, token, language);

    public static Task<ApiResponse> PutAsync(HttpClient client, string path, object body, string? token) =>
        ProductApi.PutAsync(client, path, body, token);
}

internal sealed record AdminSession(string AccessToken, string RefreshToken, Guid UserId);
