namespace Stockly.Contracts.Products;

/// <summary>P6 and P7 body. <see cref="Factor"/> is a factor string (&gt; 0, up to 6 decimals), <see cref="SellPrice"/> money.</summary>
public sealed record ProductUnitRequest(string Name, string Factor, string SellPrice, string? Barcode = null);
