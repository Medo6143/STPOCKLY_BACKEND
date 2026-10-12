using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>StockService</c> on a real PostgreSQL: the row lock, unit conversion, the tenant filter, and quantity and ledger
/// saved together (AGENTS.md rules 1, 2, 4, 5).
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class StockServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Adjust_InAnotherUnit_SavesBaseQuantityAndMovementTogether()
    {
        var (shop, product, box) = await CreateProductWithBoxAsync();

        var result = await InTransactionAsync(shop, (stock, token) =>
            stock.AdjustAsync(product.Id, box.Id, 2m, "  Found two boxes ", Catalog.UserId, token));

        result.Value.QuantityChange.Should().Be(6m);
        (await QuantityAsync(product.Id)).Should().Be(6m);
        var row = await fixture.ScalarAsync<string>(
            "SELECT concat_ws('|', quantity_change, balance_after, reason, note) FROM stock_movements WHERE id = @p0", result.Value.Id);
        row.Should().Be("6.000|6.000|3|Found two boxes");
    }

    [Fact]
    public async Task Count_DifferentQuantity_RecordsStockCountAndReturnsNewQuantity()
    {
        var (shop, product, box) = await CreateProductWithBoxAsync();

        var result = await InTransactionAsync(shop, (stock, token) => stock.CountAsync(product.Id, box.Id, 3m, Catalog.UserId, token));

        result.Value.Quantity.Should().Be(9m);
        result.Value.Outcome.Movement!.Reason.Should().Be(MovementReason.StockCount);
        (await QuantityAsync(product.Id)).Should().Be(9m);
    }

    [Fact]
    public async Task Count_SameQuantity_WritesNoMovement()
    {
        var (shop, product, _) = await CreateProductWithBoxAsync();

        var result = await InTransactionAsync(shop, (stock, token) => stock.CountAsync(product.Id, null, 0m, Catalog.UserId, token));

        result.Value.Outcome.IsUnchanged.Should().BeTrue();
        (await MovementCountAsync(product.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Adjust_OtherShopsProduct_ReturnsNotFoundAndChangesNothing()
    {
        var (_, product, _) = await CreateProductWithBoxAsync();
        var otherShop = await fixture.CreateShopAsync("متجر آخر");

        var result = await InTransactionAsync(otherShop, (stock, token) =>
            stock.AdjustAsync(product.Id, null, 5m, "note", Catalog.UserId, token));

        result.Error.Should().Be(ProductErrors.NotFound);
        (await QuantityAsync(product.Id)).Should().Be(0m);
    }

    [Fact]
    public async Task Adjust_UnknownUnit_ReturnsUnitNotFound()
    {
        var (shop, product, _) = await CreateProductWithBoxAsync();

        var result = await InTransactionAsync(shop, (stock, token) =>
            stock.AdjustAsync(product.Id, Guid.CreateVersion7(), 1m, "note", Catalog.UserId, token));

        result.Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Fact]
    public async Task OpeningStock_SavedWithTheNewProductInOneTransaction()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);

        await InTransactionAsync(shop, async (stock, token, scope) =>
        {
            scope.GetRequiredService<IProductRepository>().Add(product);
            var outcome = stock.RecordOpeningStock(product, 24m, Catalog.UserId);
            await scope.GetRequiredService<IUnitOfWork>().SaveChangesAsync(token);
            return outcome;
        });

        (await QuantityAsync(product.Id)).Should().Be(24m);
        (await fixture.ScalarAsync<string>("SELECT concat_ws('|', reason, note, balance_after) FROM stock_movements WHERE product_id = @p0", product.Id))
            .Should().Be($"4|{Product.OpeningStockNote}|24.000");
    }

    [Fact]
    public async Task Adjust_OutsideATransaction_Throws()
    {
        var (shop, product, _) = await CreateProductWithBoxAsync();
        await using var scope = fixture.CreateScope(shop.Id);
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        var act = () => stock.AdjustAsync(product.Id, null, 1m, "note", Catalog.UserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*transaction*");
    }

    [Fact]
    public async Task Adjust_WhileAnotherTransactionHoldsTheRow_WaitsAndChainsOnItsBalance()
    {
        var (shop, product, _) = await CreateProductWithBoxAsync();
        await using var scopeA = fixture.CreateScope(shop.Id);
        await using var scopeB = fixture.CreateScope(shop.Id);
        var dbA = scopeA.ServiceProvider.GetRequiredService<StocklyDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<StocklyDbContext>();

        await using var transactionA = await dbA.Database.BeginTransactionAsync();
        var first = await scopeA.ServiceProvider.GetRequiredService<IStockService>()
            .AdjustAsync(product.Id, null, 5m, "first", Catalog.UserId, CancellationToken.None);

        await using var transactionB = await dbB.Database.BeginTransactionAsync();
        var secondTask = scopeB.ServiceProvider.GetRequiredService<IStockService>()
            .AdjustAsync(product.Id, null, -2m, "second", Catalog.UserId, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        secondTask.IsCompleted.Should().BeFalse("the second transaction waits for the first one's row lock");

        await dbA.SaveChangesAsync();
        await transactionA.CommitAsync();
        var second = await secondTask;
        await dbB.SaveChangesAsync();
        await transactionB.CommitAsync();

        first.Value.BalanceAfter.Should().Be(5m);
        second.Value.BalanceAfter.Should().Be(3m, "the second movement is read after the first one committed");
        (await QuantityAsync(product.Id)).Should().Be(3m);
    }

    [Fact]
    public async Task Adjust_ManyConcurrentScopes_EndWithCorrectQuantityAndGapFreeBalanceChain()
    {
        const int Writers = 10;
        var (shop, product, _) = await CreateProductWithBoxAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
            InTransactionAsync(shop, (stock, token) => stock.AdjustAsync(product.Id, null, 1m, "parallel", Catalog.UserId, token)))));

        results.Should().AllSatisfy(result => result.IsSuccess.Should().BeTrue());
        (await QuantityAsync(product.Id)).Should().Be(Writers);
        var balances = await fixture.ColumnAsync(
            $"SELECT balance_after::text AS balance FROM stock_movements WHERE product_id = '{product.Id}' ORDER BY balance_after");
        balances.Should().Equal(Enumerable.Range(1, Writers).Select(n => $"{n}.000"), "each movement starts from the previous balance");
    }

    private async Task<(Domain.Shops.Shop Shop, Product Product, ProductUnit Box)> CreateProductWithBoxAsync()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        await using var context = fixture.CreateContext(shop.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return (shop, product, box);
    }

    private Task<Result<T>> InTransactionAsync<T>(
        Domain.Shops.Shop shop, Func<IStockService, CancellationToken, Task<Result<T>>> operation) =>
        InTransactionAsync(shop, (stock, token, _) => operation(stock, token));

    private async Task<Result<T>> InTransactionAsync<T>(
        Domain.Shops.Shop shop, Func<IStockService, CancellationToken, IServiceProvider, Task<Result<T>>> operation)
    {
        await using var scope = fixture.CreateScope(shop.Id);
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var result = await operation(stock, token, scope.ServiceProvider);
                if (result.IsSuccess)
                {
                    await unitOfWork.SaveChangesAsync(token);
                }

                return result;
            },
            result => result.IsSuccess,
            CancellationToken.None);
    }

    private Task<decimal> QuantityAsync(Guid productId) =>
        fixture.ScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @p0", productId);

    private Task<long> MovementCountAsync(Guid productId) =>
        fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE product_id = @p0", productId);
}
