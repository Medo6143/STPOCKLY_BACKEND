using System.Text.Json.Serialization;

namespace Stockly.Application.Abstractions.Ai;

/// <summary>
/// The JSON object the extraction prompt asks the model for (docs/design/backend.md §5.5). It is our contract with the
/// model, not with OpenRouter. Untrusted: parse with <see cref="AiJson.Options"/>, then validate (AGENTS.md rule 15).
/// Numbers and dates are strings exactly as the model wrote them; the normalizer parses them.
/// </summary>
public sealed record ExtractedInvoice(
    [property: JsonPropertyName("supplierName")] string? SupplierName,
    [property: JsonPropertyName("invoiceNumber")] string? InvoiceNumber,
    [property: JsonPropertyName("invoiceDate")] string? InvoiceDate,
    [property: JsonPropertyName("totalAmount")] string? TotalAmount,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedInvoiceLine> Items);

/// <summary>One product line as the model read it. Every field may be null.</summary>
public sealed record ExtractedInvoiceLine(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("quantity")] string? Quantity,
    [property: JsonPropertyName("unitPrice")] string? UnitPrice,
    [property: JsonPropertyName("lineTotal")] string? LineTotal,
    [property: JsonPropertyName("expiryDate")] string? ExpiryDate,
    [property: JsonPropertyName("batchNo")] string? BatchNo);
