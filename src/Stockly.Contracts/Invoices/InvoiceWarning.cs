namespace Stockly.Contracts.Invoices;

/// <summary>Review hints on an extracted invoice (contract §6). Serialized as the PascalCase name.</summary>
public enum InvoiceWarning
{
    TotalMismatch = 1,
    ItemCapExceeded = 2,
    UnreadableLines = 3,
    DuplicateInvoiceNumber = 4,
}
