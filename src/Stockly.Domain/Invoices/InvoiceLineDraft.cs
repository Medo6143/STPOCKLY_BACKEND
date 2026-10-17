namespace Stockly.Domain.Invoices;

/// <summary>
/// One readable line before it becomes an <see cref="InvoiceItem"/>: name as written, quantity in the line's unit,
/// price of one unit. Expiry and batch are informational (ADR-0004).
/// </summary>
public sealed record InvoiceLineDraft(string RawName, decimal Quantity, decimal UnitPrice, DateOnly? ExpiryDate, string? BatchNo);
