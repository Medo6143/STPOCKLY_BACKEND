namespace Stockly.Domain.Invoices;

/// <summary>Header fields read from the invoice. Every field is optional: the model returns null when unsure.</summary>
public sealed record InvoiceHeader(string? SupplierName, string? InvoiceNumber, DateOnly? InvoiceDate, decimal? TotalAmount)
{
    public static InvoiceHeader Empty { get; } = new(null, null, null, null);
}
