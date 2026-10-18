namespace Stockly.Admin.Configuration;

/// <summary>
/// Settings read from <c>wwwroot/appsettings.json</c>. The file is public (the browser downloads it), so it holds no
/// secrets (AGENTS.md rule 11).
/// </summary>
public sealed class AdminOptions
{
    /// <summary>Configuration key of the API's absolute base address, for example <c>http://127.0.0.1:5000/</c>.</summary>
    public const string ApiBaseUrlKey = "ApiBaseUrl";

    private const string TrailingSlash = "/";

    private AdminOptions(Uri apiBaseUrl) => ApiBaseUrl = apiBaseUrl;

    /// <summary>The API's base address, always ending with a slash so relative paths combine onto it.</summary>
    public Uri ApiBaseUrl { get; }

    /// <summary>Checks the setting; a missing or relative <c>ApiBaseUrl</c> stops start-up with a clear message.</summary>
    public static AdminOptions From(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"'{ApiBaseUrlKey}' in wwwroot/appsettings.json must be the absolute URL of the Stockly API.");
        }

        return uri.AbsoluteUri.EndsWith(TrailingSlash, StringComparison.Ordinal)
            ? new AdminOptions(uri)
            : new AdminOptions(new Uri(uri.AbsoluteUri + TrailingSlash));
    }
}
