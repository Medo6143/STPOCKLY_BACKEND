using Stockly.Domain.Products;

namespace Stockly.Domain.Tests;

internal static class TestData
{
    public static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-000000000001");
    public static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-000000000002");
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 30, 0, TimeSpan.FromHours(3));

    public const string ProductName = "بنادول اكسترا";
    public const string BaseUnitName = "شريط";

    public static Product CreateProduct(
        string name = ProductName,
        string baseUnitName = BaseUnitName,
        decimal costPrice = 12.50m,
        decimal baseUnitSellPrice = 15m,
        decimal lowStockThreshold = 5m) =>
        Product.Create(ShopId, name, baseUnitName, costPrice, baseUnitSellPrice, lowStockThreshold, Now).Value;
}
