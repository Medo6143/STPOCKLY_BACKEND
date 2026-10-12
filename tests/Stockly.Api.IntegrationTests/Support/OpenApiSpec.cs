using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Reads the runtime OpenAPI document and the committed copy (docs/api/openapi.json).</summary>
internal static class OpenApiSpec
{
    public const string CommittedPath = "docs/api/openapi.json";

    // Never used to connect: serving /openapi/v1.json touches neither PostgreSQL nor Redis.
    private const string UnusedPostgres = "Host=not-used;Database=not-used";
    private const string UnusedRedis = "not-used:6379,abortConnect=false";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string CommittedFile => ComposeImages.RepoFile(CommittedPath);

    /// <summary>The document the running Api serves, without "servers" (it names the host that served it).</summary>
    public static async Task<JsonObject> LoadRuntimeAsync()
    {
        await using var factory = new StocklyApiFactory(UnusedPostgres, UnusedRedis, includeTestEndpoints: false);
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)))!.AsObject();
        document.Remove("servers");
        return document;
    }

    public static JsonObject LoadCommitted() =>
        JsonNode.Parse(File.ReadAllText(CommittedFile))!.AsObject();

    /// <summary>Stable text form: indented, LF line endings, trailing newline.</summary>
    public static string Serialize(JsonNode document) =>
        document.ToJsonString(Format).ReplaceLineEndings("\n") + "\n";
}
