namespace Stockly.Contracts.Invoices;

/// <summary>
/// A row of the invoice list and the scan response (contract §6). <see cref="TotalAmount"/> is a money string
/// (2 decimals); <see cref="InvoiceDate"/> is <c>yyyy-MM-dd</c>; <see cref="ItemCount"/> is 0 while processing.
/// </summary>
public sealed record InvoiceListItem(
    Guid Id,
    InvoiceStatus Status,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? TotalAmount,
    int ItemCount,
    IReadOnlyList<InvoiceWarning> Warnings,
    DateTime CreatedAt);
