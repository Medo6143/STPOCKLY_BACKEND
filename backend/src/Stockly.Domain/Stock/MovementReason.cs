namespace Stockly.Domain.Stock;

/// <summary>Why stock changed. Stored as smallint: values are never reordered, only appended.</summary>
public enum MovementReason
{
    InvoiceReceipt = 1,
    Sale = 2,
    ManualAdjustment = 3,
    StockCount = 4,
    InvoiceReversal = 5,
}
