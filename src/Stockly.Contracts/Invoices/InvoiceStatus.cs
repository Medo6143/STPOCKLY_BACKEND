namespace Stockly.Contracts.Invoices;

/// <summary>Invoice lifecycle. Serialized as the PascalCase name, never a number (contract §1.1).</summary>
public enum InvoiceStatus
{
    Processing = 1,
    NeedsReview = 2,
    Confirmed = 3,
    Failed = 4,
}
