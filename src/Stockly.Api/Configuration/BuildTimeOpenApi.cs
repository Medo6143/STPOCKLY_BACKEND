using System.Reflection;

namespace Stockly.Api.Configuration;

/// <summary>
/// The build runs the app through GetDocument.Insider to write docs/api/openapi.json. That run only builds the
/// host, never starts it, and has no secrets, so required settings are replaced by placeholders that are never
/// used to connect.
/// </summary>
public static class BuildTimeOpenApi
{
    public const string DocumentName = "v1";

    private const string GeneratorAssembly = "GetDocument.Insider";
    private const string PlaceholderConnectionString = "build-time-openapi-generation";

    public static bool IsGenerating => Assembly.GetEntryAssembly()?.GetName().Name == GeneratorAssembly;

    public static string ConnectionString(IConfiguration configuration, string name) =>
        IsGenerating ? PlaceholderConnectionString : configuration.GetRequiredConnectionString(name);
}
