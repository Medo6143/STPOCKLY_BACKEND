namespace Stockly.Domain.Stock;

/// <summary>Result of a stock count. When the count matches the quantity there is no movement (no zero movements).</summary>
public sealed record StockCountOutcome(StockMovement? Movement)
{
    public static StockCountOutcome Unchanged { get; } = new((StockMovement?)null);

    public bool IsUnchanged => Movement is null;
}
