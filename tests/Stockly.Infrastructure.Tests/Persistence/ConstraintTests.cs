using Npgsql;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// The database is the last safety net: each ERD constraint must reject bad rows even when the Domain is bypassed.
/// Rows are written with raw SQL on purpose.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ConstraintTests(PostgresFixture fixture)
{
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;
    private const string CheckViolation = PostgresErrorCodes.CheckViolation;

    private const string InsertUnit =
        "INSERT INTO product_units (id, shop_id, product_id, name, factor, sell_price, is_base, barcode) " +
        "VALUES (@p0, @p1, @p2, @p3, @p4, 10, @p5, @p6)";

    private const string InsertMovement =
        "INSERT INTO stock_movements (id, shop_id, product_id, quantity_change, balance_after, reason, " +
        "invoice_id, invoice_item_id, sale_id, sale_item_id, created_by_user_id, created_at) " +
        "VALUES (@p0, @p1, @p2, @p3, @p3, @p4, @p5, @p6, @p7, @p8, @p9, now())";

    [Fact]
    public async Task ProductUnits_SecondBaseUnit_Rejected()
    {
        var product = await SaveProductAsync();

        var act = () => InsertUnitAsync(product, "علبة", 1m, isBase: true, barcode: null);

        await ShouldViolate(act, UniqueViolation, "ux_product_units_product_id_base");
    }

    [Fact]
    public async Task ProductUnits_DuplicateNameInProduct_Rejected()
    {
        var product = await SaveProductAsync();

        var act = () => InsertUnitAsync(product, "شريط", 2m, isBase: false, barcode: null);

        await ShouldViolate(act, UniqueViolation, "ux_product_units_product_id_name");
    }

    [Fact]
    public async Task ProductUnits_DuplicateBarcodeInShopAcrossProducts_Rejected()
    {
        var shop = await fixture.CreateShopAsync();
        var first = await SaveProductAsync(shop.Id, "منتج أول");
        var second = await SaveProductAsync(shop.Id, "منتج ثان");
        await InsertUnitAsync(first, "علبة", 3m, isBase: false, barcode: "6220000000001");

        var act = () => InsertUnitAsync(second, "علبة", 3m, isBase: false, barcode: "6220000000001");

        await ShouldViolate(act, UniqueViolation, "ux_product_units_shop_id_barcode");
    }

    [Fact]
    public async Task ProductUnits_SameBarcodeInDifferentShops_Allowed()
    {
        var inShopA = await SaveProductAsync();
        var inShopB = await SaveProductAsync();
        await InsertUnitAsync(inShopA, "علبة", 3m, isBase: false, barcode: "6220000000002");

        var act = () => InsertUnitAsync(inShopB, "علبة", 3m, isBase: false, barcode: "6220000000002");

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task ProductUnits_FactorNotPositive_Rejected(string factor)
    {
        var product = await SaveProductAsync();

        var act = () => InsertUnitAsync(product, "علبة", decimal.Parse(factor, System.Globalization.CultureInfo.InvariantCulture), isBase: false, barcode: null);

        await ShouldViolate(act, CheckViolation, "ck_product_units_factor");
    }

    [Fact]
    public async Task Products_NegativeLowStockThreshold_Rejected()
    {
        var product = await SaveProductAsync();

        var act = () => fixture.ExecuteAsync("UPDATE products SET low_stock_threshold = -1 WHERE id = @p0", product.Id);

        await ShouldViolate(act, CheckViolation, "ck_products_low_stock_threshold");
    }

    [Fact]
    public async Task StockMovements_DuplicateInvoiceItemAndReason_Rejected()
    {
        var product = await SaveProductAsync();
        var (invoiceId, itemId) = await fixture.CreateInvoiceLineAsync(product.ShopId);
        await InsertMovementAsync(product, 5m, MovementReason.InvoiceReceipt, invoiceId, itemId, null, null);

        var act = () => InsertMovementAsync(product, 5m, MovementReason.InvoiceReceipt, invoiceId, itemId, null, null);

        await ShouldViolate(act, UniqueViolation, "ux_stock_movements_invoice_item_id_reason");
    }

    [Fact]
    public async Task StockMovements_ReceiptThenReversalOfSameLine_Allowed()
    {
        var product = await SaveProductAsync();
        var (invoiceId, itemId) = await fixture.CreateInvoiceLineAsync(product.ShopId);
        await InsertMovementAsync(product, 5m, MovementReason.InvoiceReceipt, invoiceId, itemId, null, null);

        var reversal = () => InsertMovementAsync(product, -5m, MovementReason.InvoiceReversal, invoiceId, itemId, null, null);
        await reversal.Should().NotThrowAsync();

        var secondReversal = () => InsertMovementAsync(product, -5m, MovementReason.InvoiceReversal, invoiceId, itemId, null, null);
        await ShouldViolate(secondReversal, UniqueViolation, "ux_stock_movements_invoice_item_id_reason");
    }

    [Fact]
    public async Task StockMovements_DuplicateSaleItem_Rejected()
    {
        var product = await SaveProductAsync();
        var (saleId, itemId) = await fixture.CreateSaleLineAsync(product.ShopId, product.Id, product.BaseUnit.Id);
        await InsertMovementAsync(product, -1m, MovementReason.Sale, null, null, saleId, itemId);

        var act = () => InsertMovementAsync(product, -1m, MovementReason.Sale, null, null, saleId, itemId);

        await ShouldViolate(act, UniqueViolation, "ux_stock_movements_sale_item_id");
    }

    [Fact]
    public async Task StockMovements_ZeroChange_Rejected()
    {
        var product = await SaveProductAsync();

        var act = () => InsertMovementAsync(product, 0m, MovementReason.StockCount, null, null, null, null);

        await ShouldViolate(act, CheckViolation, "ck_stock_movements_quantity_change");
    }

    [Fact]
    public async Task StockMovements_BothInvoiceAndSaleLine_Rejected()
    {
        var product = await SaveProductAsync();

        var act = () => InsertMovementAsync(product, 1m, MovementReason.InvoiceReceipt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await ShouldViolate(act, CheckViolation, "ck_stock_movements_single_source");
    }

    private static async Task ShouldViolate(Func<Task> act, string sqlState, string constraint)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraint);
    }

    private async Task<Product> SaveProductAsync(Guid? shopId = null, string name = "بنادول اكسترا")
    {
        var shop = shopId ?? (await fixture.CreateShopAsync()).Id;
        var product = Catalog.NewProduct(shop, name);
        await using var context = fixture.CreateContext(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private Task<int> InsertUnitAsync(Product product, string name, decimal factor, bool isBase, string? barcode) =>
        fixture.ExecuteAsync(
            InsertUnit, Guid.NewGuid(), product.ShopId, product.Id, name, factor, isBase, (object?)barcode ?? DBNull.Value);

    private Task<int> InsertMovementAsync(
        Product product, decimal change, MovementReason reason, Guid? invoiceId, Guid? invoiceItemId, Guid? saleId, Guid? saleItemId) =>
        fixture.ExecuteAsync(
            InsertMovement,
            Guid.NewGuid(),
            product.ShopId,
            product.Id,
            change,
            (short)reason,
            (object?)invoiceId ?? DBNull.Value,
            (object?)invoiceItemId ?? DBNull.Value,
            (object?)saleId ?? DBNull.Value,
            (object?)saleItemId ?? DBNull.Value,
            Catalog.UserId);
}
