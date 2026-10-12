namespace Stockly.Contracts.Products;

/// <summary>Why stock changed. Serialized as the PascalCase name, never a number (contract §1.1).</summary>
public enum MovementReason
{
    InvoiceReceipt = 1,
    Sale = 2,
    ManualAdjustment = 3,
    StockCount = 4,
    InvoiceReversal = 5,
}
