namespace Stockly.Contracts.Sales;

/// <summary>A row of the sales list (contract §7, S2). <see cref="TotalAmount"/> is a money string (2 decimals).</summary>
public sealed record SaleListItem(Guid Id, string TotalAmount, int ItemCount, DateTime CreatedAt);
