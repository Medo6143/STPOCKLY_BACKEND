using Stockly.Domain.Products;
using Stockly.Domain.Stock;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>Builds valid domain objects for persistence tests.</summary>
internal static class Catalog
{
    public static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000ff");

    public static Product NewProduct(Guid shopId, string name = "بنادول اكسترا") =>
        Product.Create(shopId, name, "شريط", 12.50m, 15m, 5m, TestTime.T0).Value;

    public static StockMovement Receive(Product product, decimal quantity) =>
        product.RecordMovement(
            quantity,
            MovementReason.InvoiceReceipt,
            MovementSource.FromInvoiceItem(Guid.NewGuid(), Guid.NewGuid()),
            UserId,
            note: null,
            TestTime.T1).Value;
}
