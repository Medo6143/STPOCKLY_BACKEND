using System.Text.Encodings.Web;
using System.Text.Json;

namespace Stockly.Admin.Formatting;

/// <summary>Indents JSON for the read-only code views (raw AI output, audit before/after). Invalid JSON is shown as is.</summary>
public static class JsonText
{
    // Arabic names stay readable instead of escaped. Safe here: Blazor HTML-encodes the text it renders.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Pretty(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        try
        {
            using var document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    public static string Pretty(JsonElement element) => JsonSerializer.Serialize(element, Indented);
}
