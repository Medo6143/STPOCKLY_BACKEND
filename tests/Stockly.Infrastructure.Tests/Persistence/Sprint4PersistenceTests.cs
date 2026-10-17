using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;
using Stockly.Domain.Suppliers;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// The Sprint 4 persistence pieces on a real PostgreSQL: the ordered multi-product lock, the receipt and sale movements,
/// the supplier find-or-create, the alias upsert and the sale tables' constraints (docs/design/backend.md §11, erd §3.4).
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class Sprint4PersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task LockProducts_ThenReceiveAndSell_ChainsBalancesAndSavesTheLedger()
    {
        var shop = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);
        var line = await fixture.CreateInvoiceLineAsync(shop.Id);

        await using var scope = fixture.CreateScope(shop.Id);
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var locked = await stock.LockProductsAsync([product.Id, Guid.CreateVersion7()], token);
                locked.Keys.Should().Equal(product.Id);
                var receipt = stock.ReceiveFromInvoice(locked[product.Id], 5m, line.InvoiceId, line.ItemId, Catalog.UserId);
                receipt.Value.BalanceAfter.Should().Be(5m);
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success();
            },
            result => result.IsSuccess,
            CancellationToken.None);

        (await fixture.ScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @p0", product.Id)).Should().Be(5m);
        (await fixture.ScalarAsync<string>("SELECT concat_ws('|', reason, quantity_change, balance_after) FROM stock_movements WHERE product_id = @p0", product.Id))
            .Should().Be("1|5.000|5.000");
    }

    [Fact]
    public async Task LockProducts_AnotherShopsProduct_IsNotLockedOrReturned()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(other.Id);

        await using var scope = fixture.CreateScope(shop.Id);
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();
        var locked = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => stock.LockProductsAsync([product.Id], token), _ => false, CancellationToken.None);

        locked.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordSale_ProductNotLocked_Throws()
    {
        var shop = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);

        await using var scope = fixture.CreateScope(shop.Id);
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();
        var tracked = await scope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(product.Id, CancellationToken.None);

        var act = () => stock.RecordSale(tracked!, 1m, Guid.CreateVersion7(), Guid.CreateVersion7(), Catalog.UserId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LockProducts_OutsideATransaction_Throws()
    {
        var shop = await fixture.CreateShopAsync();
        await using var scope = fixture.CreateScope(shop.Id);

        var act = () => scope.ServiceProvider.GetRequiredService<IStockService>().LockProductsAsync([Guid.CreateVersion7()], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SupplierFindOrCreate_NewThenSameNormalizedName_CreatesOnce()
    {
        var shop = await fixture.CreateShopAsync();

        var first = await FindOrCreateAsync(shop.Id, "شركة الأدوية");
        var second = await FindOrCreateAsync(shop.Id, "  شركه الادويه ");

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        second.SupplierId.Should().Be(first.SupplierId);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM suppliers WHERE shop_id = @p0", shop.Id)).Should().Be(1);
    }

    [Fact]
    public async Task AliasUpsert_NewThenExisting_InsertsThenRepointsAndAddsConfirmations()
    {
        var shop = await fixture.CreateShopAsync();
        var first = await SavedProductAsync(shop.Id, "أول");
        var second = await SavedProductAsync(shop.Id, "ثاني");
        var supplier = await FindOrCreateAsync(shop.Id, "مورد");

        await UpsertAsync(shop.Id, ProductAlias.Learn(shop.Id, first.Id, null, first.BaseUnit.Id, "اسم", TestTime.T0, 2).Value);
        await UpsertAsync(shop.Id, ProductAlias.Learn(shop.Id, second.Id, null, second.BaseUnit.Id, " اسم ", TestTime.T1).Value);
        await UpsertAsync(shop.Id, ProductAlias.Learn(shop.Id, first.Id, supplier.SupplierId, first.BaseUnit.Id, "اسم", TestTime.T1).Value);

        var rows = await fixture.ColumnAsync(
            $"SELECT concat_ws('|', product_id, supplier_id IS NULL, times_confirmed) FROM product_aliases WHERE shop_id = '{shop.Id}' ORDER BY supplier_id NULLS FIRST");
        rows.Should().Equal($"{second.Id}|t|3", $"{first.Id}|f|1");
    }

    [Fact]
    public async Task Sale_SavedWithLines_RoundTripsAndTheClientRequestIdIsUniquePerShop()
    {
        var shop = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);
        var clientRequestId = Guid.CreateVersion7();
        var sale = Sale.Start(shop.Id, clientRequestId, Catalog.UserId, null, TestTime.T1).Value;
        sale.AddItem(product, product.BaseUnit, 1.5m, 14m).IsSuccess.Should().BeTrue();
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Add(sale);
            await context.SaveChangesAsync();
        }

        (await fixture.ScalarAsync<string>("SELECT concat_ws('|', line_no, quantity, list_unit_price, unit_price, line_total) FROM sale_items WHERE sale_id = @p0", sale.Id))
            .Should().Be("1|1.500|15.00|14.00|21.00");
        var duplicate = () => fixture.ExecuteAsync(
            "INSERT INTO sales (id, shop_id, client_request_id, created_by_user_id, total_amount, created_at) VALUES (@p0, @p1, @p2, @p3, 0, now())",
            Guid.CreateVersion7(),
            shop.Id,
            clientRequestId,
            Catalog.UserId);
        await duplicate.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "ux_sales_shop_id_client_request_id");
    }

    [Fact]
    public async Task SaleItems_NegativeQuantity_RejectedByTheCheck()
    {
        var shop = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);
        var saleId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO sales (id, shop_id, client_request_id, created_by_user_id, total_amount, created_at) VALUES (@p0, @p1, @p2, @p3, 0, now())",
            saleId,
            shop.Id,
            Guid.CreateVersion7(),
            Catalog.UserId);

        var act = () => fixture.ExecuteAsync(
            "INSERT INTO sale_items (id, shop_id, sale_id, line_no, product_id, product_unit_id, quantity, list_unit_price, unit_price, line_total) " +
            "VALUES (@p0, @p1, @p2, 1, @p3, @p4, -1, 1, 1, 1)",
            Guid.CreateVersion7(),
            shop.Id,
            saleId,
            product.Id,
            product.BaseUnit.Id);

        await act.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "ck_sale_items_amounts");
    }

    [Fact]
    public async Task UnitUsage_InvoiceAndSaleLines_FoundOnlyInTheirShop()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);
        var box = await SavedUnitAsync(shop.Id, product.Id);
        var unused = await SavedUnitAsync(shop.Id, product.Id, "كرتونة");
        await fixture.CreateSaleLineAsync(shop.Id, product.Id, box);
        var (_, itemId) = await fixture.CreateInvoiceLineAsync(shop.Id);
        await fixture.ExecuteAsync("UPDATE invoice_items SET product_id = @p0, product_unit_id = @p1 WHERE id = @p2", product.Id, unused, itemId);

        await using var scope = fixture.CreateScope(shop.Id);
        var usage = scope.ServiceProvider.GetRequiredService<IProductUnitUsageQueries>();
        await using var otherScope = fixture.CreateScope(other.Id);
        var otherUsage = otherScope.ServiceProvider.GetRequiredService<IProductUnitUsageQueries>();

        (await usage.IsUsedBySaleLinesAsync(box, CancellationToken.None)).Should().BeTrue();
        (await usage.IsUsedByInvoiceLinesAsync(box, CancellationToken.None)).Should().BeFalse();
        (await usage.IsUsedByInvoiceLinesAsync(unused, CancellationToken.None)).Should().BeTrue();
        (await usage.IsUsedBySaleLinesAsync(unused, CancellationToken.None)).Should().BeFalse();
        (await otherUsage.IsUsedBySaleLinesAsync(box, CancellationToken.None)).Should().BeFalse("tenant-filtered");
    }

    [Fact]
    public async Task TrySaveChanges_UnitDeletedWhileASaleLineReferencesIt_ReturnsInUse()
    {
        var shop = await fixture.CreateShopAsync();
        var product = await SavedProductAsync(shop.Id);
        var box = await SavedUnitAsync(shop.Id, product.Id);

        await using var scope = fixture.CreateScope(shop.Id);
        var tracked = await scope.ServiceProvider.GetRequiredService<IProductRepository>().FindAsync(product.Id, CancellationToken.None);
        tracked!.RemoveUnit(box).IsSuccess.Should().BeTrue();
        await fixture.CreateSaleLineAsync(shop.Id, product.Id, box);

        var saved = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync(CancellationToken.None);

        saved.Error.Should().Be(ProductUnitErrors.InUse);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE id = @p0", box)).Should().Be(1);
    }

    private async Task<Guid> SavedUnitAsync(Guid shopId, Guid productId, string name = "علبة")
    {
        var unitId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO product_units (id, shop_id, product_id, name, factor, sell_price, is_base) VALUES (@p0, @p1, @p2, @p3, 3, 40, false)",
            unitId,
            shopId,
            productId,
            name);
        return unitId;
    }

    private async Task<Product> SavedProductAsync(Guid shopId, string name = "بنادول اكسترا")
    {
        var product = Catalog.NewProduct(shopId, name);
        await using var context = fixture.CreateContext(shopId);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private async Task<SupplierResolution> FindOrCreateAsync(Guid shopId, string name)
    {
        await using var scope = fixture.CreateScope(shopId);
        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierRepository>();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => suppliers.FindOrCreateAsync(Supplier.Create(shopId, name, null, TestTime.T0).Value, token),
            _ => true,
            CancellationToken.None);
    }

    private async Task UpsertAsync(Guid shopId, ProductAlias alias)
    {
        await using var scope = fixture.CreateScope(shopId);
        await scope.ServiceProvider.GetRequiredService<IProductAliasRepository>().UpsertAsync([alias], CancellationToken.None);
    }
}
