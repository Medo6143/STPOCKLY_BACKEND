namespace Stockly.Domain.Invoices;

/// <summary>
/// One readable line before it becomes an <see cref="InvoiceItem"/>: name as written, quantity in the line's unit,
/// price of one unit. Expiry and batch are informational (ADR-0004). <see cref="Match"/> is the product the matcher found
/// (docs/design/backend.md §8); <c>null</c> leaves the line unmatched.
/// </summary>
public sealed record InvoiceLineDraft(
    string RawName, decimal Quantity, decimal UnitPrice, DateOnly? ExpiryDate, string? BatchNo, InvoiceLineMatch? Match = null);

/// <summary>An automatic match: an existing product, one of its units, and the confidence (0.00–1.00, 2 decimals).</summary>
public sealed record InvoiceLineMatch(Guid ProductId, Guid ProductUnitId, decimal Confidence);
