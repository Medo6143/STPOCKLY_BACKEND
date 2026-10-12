namespace Stockly.Domain.Text;

/// <summary>Optional free text (barcodes, categories): trimmed, and blank becomes <c>null</c>.</summary>
public static class OptionalText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
