using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stockly.Admin.Services;

/// <summary>
/// JSON exactly as the contract writes it (§1.1): camelCase names, enums as PascalCase names, strict numbers. Money and
/// costs are strings in the contract and stay strings here.
/// </summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
