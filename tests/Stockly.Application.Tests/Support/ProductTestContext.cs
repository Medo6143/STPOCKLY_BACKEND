using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;

namespace Stockly.Application.Tests.Support;

/// <summary>
/// Substitutes for every port the product handlers use: an Owner of <see cref="ShopId"/> signed in, a fixed clock.
/// The repositories are tenant-filtered in production, so "another shop's product" is a repository returning null.
/// </summary>
internal sealed class ProductTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
    public static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a1");
    public static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");
    public static readonly Guid OtherShopsProductId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000c9");

    public ProductTestContext()
    {
        Clock.UtcNow.Returns(Now);
        UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
        CurrentUser.UserId.Returns(UserId);
        CurrentUser.Role.Returns(ShopRole.Owner);
        CurrentTenant.ShopId.Returns(ShopId);
        Queries.FindDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Detail(call.Arg<Guid>()));
        Stock.RecordOpeningStock(Arg.Any<Product>(), Arg.Any<decimal>(), Arg.Any<Guid>())
            .Returns(call => call.Arg<Product>().RecordOpeningStock(call.ArgAt<decimal>(1), call.ArgAt<Guid>(2), Now));
    }

    public IClock Clock { get; } = Substitute.For<IClock>();

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    public ICurrentTenant CurrentTenant { get; } = Substitute.For<ICurrentTenant>();

    public IProductRepository Products { get; } = Substitute.For<IProductRepository>();

    public IProductQueries Queries { get; } = Substitute.For<IProductQueries>();

    public IStockService Stock { get; } = Substitute.For<IStockService>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public ICacheService Cache { get; } = Substitute.For<ICacheService>();

    public static Product NewProduct(string? barcode = null) =>
        Product.Create(ShopId, "بنادول اكسترا", "شريط", 12.50m, 15m, 5m, Now, barcode).Value;

    /// <summary>A product the tenant-filtered repository finds (and returns null for any other id).</summary>
    public Product Existing(string? barcode = null)
    {
        var product = NewProduct(barcode);
        Products.FindAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    public static ProductDetail Detail(Guid productId) => new(
        productId, "بنادول اكسترا", null, null, "0.000", "12.50", "5.000", true, false, [], Now.UtcDateTime, Now.UtcDateTime);
}
