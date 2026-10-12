using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>IUnitOfWork.TrySaveChangesAsync</c>: a barcode taken by a concurrent transaction after the handler's check is the
/// contract's 409 error, not an exception; the losing transaction rolls back. Other unique violations still throw.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class UnitOfWorkConflictTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TrySave_ProductBarcodeCommittedByAConcurrentTransaction_ReturnsDuplicateBarcodeAndRollsBack()
    {
        var shop = await fixture.CreateShopAsync();
        var winner = Product.Create(shop.Id, "winner", "u", 1m, 1m, 0m, TestTime.T0, "RACE-1").Value;
        var loser = Product.Create(shop.Id, "loser", "u", 1m, 1m, 0m, TestTime.T0, "RACE-1").Value;

        var result = await RaceAsync(shop.Id, db => db.Products.Add(winner), (scope, _) => scope.GetRequiredService<IProductRepository>().Add(loser));

        result.Error.Should().Be(ProductErrors.DuplicateBarcode);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE barcode = 'RACE-1'")).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE id = @p0", loser.Id)).Should().Be(0, "no partial product");
    }

    [Fact]
    public async Task TrySave_UnitBarcodeCommittedByAConcurrentTransaction_ReturnsUnitDuplicateBarcode()
    {
        var shop = await fixture.CreateShopAsync();
        var first = Catalog.NewProduct(shop.Id, "first");
        var second = Catalog.NewProduct(shop.Id, "second");
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.AddRange(first, second);
            await context.SaveChangesAsync();
        }

        var result = await RaceAsync(
            shop.Id,
            db => db.Products.Include(p => p.Units).Single(p => p.Id == first.Id).AddUnit("علبة", 3m, 40m, "RACE-U"),
            (scope, _) => scope.GetRequiredService<StocklyDbContext>().Products.Include(p => p.Units)
                .Single(p => p.Id == second.Id).AddUnit("علبة", 3m, 40m, "RACE-U"));

        result.Error.Should().Be(ProductUnitErrors.DuplicateBarcode);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE barcode = 'RACE-U'")).Should().Be(1);
    }

    [Fact]
    public async Task TrySave_OtherUniqueViolation_StillThrows()
    {
        var user = await fixture.CreateUserAsync();
        await using var scope = fixture.CreateScope(shopId: null);
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        tokens.Add(RefreshToken.StartFamily(user, "same-hash", null, TestTime.T0, TimeSpan.FromDays(30)));
        tokens.Add(RefreshToken.StartFamily(user, "same-hash", null, TestTime.T0, TimeSpan.FromDays(30)));

        var act = () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TrySave_NoConflict_Succeeds()
    {
        var shop = await fixture.CreateShopAsync();
        await using var scope = fixture.CreateScope(shop.Id);
        scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(Catalog.NewProduct(shop.Id));

        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Transaction A writes and holds its row uncommitted; B (the production unit of work) writes the same unique value,
    /// blocks on A's index entry, and loses when A commits.
    /// </summary>
    private async Task<Result> RaceAsync(Guid shopId, Action<StocklyDbContext> winnerWrites, Action<IServiceProvider, CancellationToken> loserWrites)
    {
        await using var winnerScope = fixture.CreateScope(shopId);
        var winnerDb = winnerScope.ServiceProvider.GetRequiredService<StocklyDbContext>();
        await using var winnerTransaction = await winnerDb.Database.BeginTransactionAsync();
        winnerWrites(winnerDb);
        await winnerDb.SaveChangesAsync();

        await using var loserScope = fixture.CreateScope(shopId);
        var unitOfWork = loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var loser = unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                loserWrites(loserScope.ServiceProvider, token);
                return await unitOfWork.TrySaveChangesAsync(token);
            },
            result => result.IsSuccess,
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        loser.IsCompleted.Should().BeFalse("the second insert waits for the first transaction's index entry");
        await winnerTransaction.CommitAsync();
        return await loser;
    }
}
