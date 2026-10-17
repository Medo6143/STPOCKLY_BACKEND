using System.Text.Json;

namespace Stockly.Application.Features.Invoices;

/// <summary>
/// Turns a model's <c>message.content</c> into a value the <c>invoices.raw_ai_json</c> jsonb column accepts
/// (docs/design/backend.md §4.2): valid JSON is kept as is; anything else (or content over 256 KB) is stored as a JSON
/// string literal, cut to 256 KB. PostgreSQL jsonb rejects NUL characters, so those are removed.
/// </summary>
public static class RawAiJson
{
    /// <summary>Longest content kept, in characters.</summary>
    public const int MaxLength = 256 * 1024;

    private const string EscapedNul = "\\u0000";

    public static string? From(string? content)
    {
        if (content is null)
        {
            return null;
        }

        if (content.Length <= MaxLength && !content.Contains(EscapedNul, StringComparison.Ordinal) && IsJson(content))
        {
            return content;
        }

        var text = content.Replace("\0", string.Empty, StringComparison.Ordinal);
        return JsonSerializer.Serialize(text.Length > MaxLength ? text[..MaxLength] : text);
    }

    private static bool IsJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
