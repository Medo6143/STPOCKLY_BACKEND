using Microsoft.EntityFrameworkCore;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class RoundTripTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Product_WithUnitsAndMovements_LoadsWithExactDecimals()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Product.Create(shop.Id, " Panadol بَنادول ", "شريط", 0.01m, 9_999_999_999_999_999.99m, 0.001m, TestTime.T0).Value;
        var box = product.AddUnit("علبة", 1.123456m, 40.05m, "6221234567890").Value;
        var drum = product.AddUnit("برميل", 99_999_999.999999m, 0m, null).Value;
        var (invoiceId, itemId) = await fixture.CreateInvoiceLineAsync(shop.Id);
        var receipt = product.RecordMovement(
            99_999_999_999.999m, MovementReason.InvoiceReceipt, MovementSource.FromInvoiceItem(invoiceId, itemId), Catalog.UserId, null, TestTime.T1).Value;

        await using (var writer = fixture.CreateContext(shop.Id))
        {
            writer.Products.Add(product);
            writer.StockMovements.Add(receipt);
            await writer.SaveChangesAsync();
        }

        // The sale line references the saved product (sale_items.product_id is a FK since migration Sales).
        var (saleId, saleItemId) = await fixture.CreateSaleLineAsync(shop.Id, product.Id, product.BaseUnit.Id);
        var sale = product.RecordMovement(
            -0.001m, MovementReason.Sale, MovementSource.FromSaleItem(saleId, saleItemId), Catalog.UserId, null, TestTime.T2).Value;
        await using (var writer = fixture.CreateContext(shop.Id))
        {
            writer.StockMovements.Add(sale);
            await writer.Database.ExecuteSqlRawAsync("UPDATE products SET quantity = {0} WHERE id = {1}", product.Quantity, product.Id);
            await writer.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext(shop.Id);
        var loaded = await reader.Products.Include(p => p.Units).SingleAsync(p => p.Id == product.Id);
        var movements = await reader.StockMovements.Where(m => m.ProductId == product.Id).OrderBy(m => m.CreatedAt).ToListAsync();

        loaded.Should().BeEquivalentTo(
            new
            {
                product.Id,
                ShopId = shop.Id,
                Name = "Panadol بَنادول",
                NormalizedName = "panadol بنادول",
                Quantity = 99_999_999_999.998m,
                CostPrice = 0.01m,
                LowStockThreshold = 0.001m,
                IsArchived = false,
                CreatedAt = TestTime.T0,
            },
            options => options.ExcludingMissingMembers());
        loaded.BaseUnit.SellPrice.Should().Be(9_999_999_999_999_999.99m);
        loaded.Units.Single(u => u.Id == box.Id).Should().BeEquivalentTo(
            new { Name = "علبة", Factor = 1.123456m, SellPrice = 40.05m, Barcode = "6221234567890", IsBase = false },
            options => options.ExcludingMissingMembers());
        loaded.Units.Single(u => u.Id == drum.Id).Factor.Should().Be(99_999_999.999999m);

        movements.Should().HaveCount(2);
        movements[0].Should().BeEquivalentTo(receipt);
        movements[1].Should().BeEquivalentTo(sale);
        movements[1].BalanceAfter.Should().Be(loaded.Quantity);
    }

    [Fact]
    public async Task RemoveUnit_SavedThroughAggregate_DeletesRow()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        await using (var writer = fixture.CreateContext(shop.Id))
        {
            writer.Products.Add(product);
            await writer.SaveChangesAsync();
        }

        await using (var editor = fixture.CreateContext(shop.Id))
        {
            var loaded = await editor.Products.Include(p => p.Units).SingleAsync(p => p.Id == product.Id);
            loaded.RemoveUnit(box.Id).IsSuccess.Should().BeTrue();
            await editor.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext(shop.Id);
        (await reader.Set<ProductUnit>().Where(u => u.ProductId == product.Id).Select(u => u.Name).ToListAsync())
            .Should().Equal("شريط");
    }

    [Fact]
    public async Task Shop_SavesAndLoadsEnumsAsSmallint()
    {
        var shop = await fixture.CreateShopAsync("محل موبايلات");

        var row = await fixture.ColumnAsync(
            $"SELECT shop_type::text || '/' || status::text || '/' || pg_typeof(shop_type)::text FROM shops WHERE id = '{shop.Id}'");

        row.Should().Equal("1/1/smallint");
    }
}
