using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stockly.Application.Abstractions.Ai;

/// <summary>
/// Strict parsing of model output into <see cref="ExtractedInvoice"/> (docs/design/backend.md §5.5): unknown properties,
/// missing required properties, null for a non-nullable property, a JSON number where a string is expected, trailing
/// commas, comments and nesting deeper than 8 all fail. Any <see cref="JsonException"/> means invalid output.
/// </summary>
public static class AiJson
{
    public const int MaxDepth = 8;

    public static JsonSerializerOptions Options { get; } = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.Strict,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaxDepth,
    };

    /// <summary>The extraction in <paramref name="json"/>; throws <see cref="JsonException"/> when it breaks any rule above.</summary>
    public static ExtractedInvoice Parse(string json) =>
        JsonSerializer.Deserialize<ExtractedInvoice>(json, Options) ?? throw new JsonException("The output is JSON null.");

    /// <summary>The extraction as JSON with the property names of the prompt's schema.</summary>
    public static string Serialize(ExtractedInvoice extraction) => JsonSerializer.Serialize(extraction, Options);
}
