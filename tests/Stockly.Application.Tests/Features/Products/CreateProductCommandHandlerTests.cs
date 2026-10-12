using NSubstitute;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Products;
using Stockly.Application.Features.Products.CreateProduct;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Stock;
using static Stockly.Application.Tests.Support.ProductTestContext;

namespace Stockly.Application.Tests.Features.Products;

public sealed class CreateProductCommandHandlerTests
{
    private readonly ProductTestContext _context = new();

    [Fact]
    public async Task Handle_ValidRequest_AddsProductOfTheTokensShopAndReturnsItsDetail()
    {
        Product? added = null;
        _context.Products.Add(Arg.Do<Product>(product => added = product));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        added.Should().NotBeNull();
        added!.ShopId.Should().Be(ShopId);
        added.Name.Should().Be("بنادول اكسترا");
        added.Barcode.Should().Be("6221234567890");
        added.Category.Should().Be("مسكنات");
        added.CostPrice.Should().Be(12.50m);
        added.BaseUnit.SellPrice.Should().Be(15m);
        added.LowStockThreshold.Should().Be(5m);
        added.CreatedAt.Should().Be(Now);
        result.Value.Id.Should().Be(added.Id);
        await _context.UnitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OwnerWithOpeningQuantity_RecordsOpeningStockThroughTheStockService()
    {
        Product? added = null;
        _context.Products.Add(Arg.Do<Product>(product => added = product));

        await Handler().Handle(Command() with { OpeningQuantity = "24.000" }, CancellationToken.None);

        _context.Stock.Received(1).RecordOpeningStock(added!, 24m, UserId);
        added!.Quantity.Should().Be(24m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.000")]
    public async Task Handle_NoOrZeroOpeningQuantity_StartsAtZero(string? opening)
    {
        Product? added = null;
        _context.Products.Add(Arg.Do<Product>(product => added = product));

        await Handler().Handle(Command() with { OpeningQuantity = opening }, CancellationToken.None);

        added!.Quantity.Should().Be(0m);
        _context.Stock.Received(1).RecordOpeningStock(added, 0m, UserId);
    }

    [Theory]
    [InlineData("24.000")]
    [InlineData("0.000")]
    public async Task Handle_StaffWithOpeningQuantity_ReturnsOwnerOnlyAndCreatesNothing(string opening)
    {
        _context.CurrentUser.Role.Returns(ShopRole.Staff);

        var result = await Handler().Handle(Command() with { OpeningQuantity = opening }, CancellationToken.None);

        result.Error.Should().Be(ProductAccessErrors.OpeningStockOwnerOnly);
        _context.Products.DidNotReceive().Add(Arg.Any<Product>());
        await _context.UnitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaffWithoutOpeningQuantity_CreatesTheProduct()
    {
        _context.CurrentUser.Role.Returns(ShopRole.Staff);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("12.505", null, null, "Precision.TooManyDecimals")]
    [InlineData("-1", null, null, "Precision.Negative")]
    [InlineData("12.50", "-1.000", null, "Precision.Negative")]
    [InlineData("12.50", null, "1.0001", "Precision.TooManyDecimals")]
    [InlineData("10000000000000000", null, null, "Precision.OutOfRange")]
    public async Task Handle_InvalidDecimal_ReturnsPrecisionErrorAndAddsNothing(
        string costPrice, string? threshold, string? opening, string code)
    {
        var command = Command() with { CostPrice = costPrice, LowStockThreshold = threshold, OpeningQuantity = opening };

        var result = await Handler().Handle(command, CancellationToken.None);

        result.Error.Code.Should().Be(code);
        _context.Products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task Handle_BlankNames_ReturnNameRequiredErrors()
    {
        (await Handler().Handle(Command() with { Name = " ـ " }, CancellationToken.None))
            .Error.Should().Be(ProductErrors.NameRequired);
        (await Handler().Handle(Command() with { BaseUnitName = " " }, CancellationToken.None))
            .Error.Should().Be(ProductUnitErrors.NameRequired);
    }

    [Fact]
    public async Task Handle_BarcodeOfAnotherActiveProduct_ReturnsDuplicateBarcode()
    {
        _context.Products.IsBarcodeUsedAsync("6221234567890", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().Handle(Command() with { Barcode = " 6221234567890 " }, CancellationToken.None);

        result.Error.Should().Be(ProductErrors.DuplicateBarcode);
        _context.Products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task Handle_BarcodeTakenConcurrentlyAtSave_ReturnsDuplicateBarcodeWithoutReadingTheDetail()
    {
        _context.UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(ProductErrors.DuplicateBarcode));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.Error.Should().Be(ProductErrors.DuplicateBarcode);
        await _context.Queries.DidNotReceive().FindDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoBarcode_DoesNotCheckIt()
    {
        await Handler().Handle(Command() with { Barcode = "  " }, CancellationToken.None);

        await _context.Products.DidNotReceive().IsBarcodeUsedAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OpeningStockRejectedByTheDomain_ReturnsItsErrorWithoutSaving()
    {
        _context.Stock.RecordOpeningStock(Arg.Any<Product>(), Arg.Any<decimal>(), Arg.Any<Guid>())
            .Returns(StockErrors.UserRequired);

        var result = await Handler().Handle(Command() with { OpeningQuantity = "1" }, CancellationToken.None);

        result.Error.Should().Be(StockErrors.UserRequired);
        await _context.UnitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserOrTenant_ReturnsUnauthenticated()
    {
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    private static CreateProductCommand Command() =>
        new("بنادول اكسترا", "شريط", "12.50", "15.00", "6221234567890", "مسكنات", "5.000", null);

    private CreateProductCommandHandler Handler() => new(
        _context.CurrentUser, _context.CurrentTenant, _context.Products, _context.Queries, _context.Stock, _context.UnitOfWork, _context.Clock);
}
