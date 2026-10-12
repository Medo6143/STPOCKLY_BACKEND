using NSubstitute;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Products.AdjustStock;
using Stockly.Application.Features.Products.CountStock;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using static Stockly.Application.Tests.Support.ProductTestContext;
using ContractMovementReason = Stockly.Contracts.Products.MovementReason;

namespace Stockly.Application.Tests.Features.Products;

/// <summary>P9 and P10 handlers: parsing, the stock service call, and the response.</summary>
public sealed class StockHandlerTests
{
    private readonly ProductTestContext _context = new();

    [Fact]
    public async Task Adjust_Valid_CallsStockServiceWithBaseValuesSavesAndReturnsMovement()
    {
        var product = NewProduct();
        var unitId = Guid.CreateVersion7();
        var movement = product.AdjustStock(null, -2m, UserId, "Expired", Now).Value;
        _context.Stock.AdjustAsync(product.Id, unitId, -2m, "Expired", UserId, Arg.Any<CancellationToken>()).Returns(movement);

        var result = await AdjustHandler().Handle(new AdjustStockCommand(product.Id, "-2.000", "Expired", unitId), CancellationToken.None);

        result.Value.QuantityChange.Should().Be("-2.000");
        result.Value.Reason.Should().Be(ContractMovementReason.ManualAdjustment);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1.0001", "Precision.TooManyDecimals")]
    [InlineData("100000000000", "Precision.OutOfRange")]
    public async Task Adjust_InvalidQuantity_ReturnsPrecisionErrorWithoutTouchingStock(string quantity, string code)
    {
        var result = await AdjustHandler().Handle(new AdjustStockCommand(Guid.CreateVersion7(), quantity, "note", null), CancellationToken.None);

        result.Error.Code.Should().Be(code);
        await _context.Stock.DidNotReceive().AdjustAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(StockErrorsFromTheService))]
    public async Task Adjust_StockServiceFails_ReturnsItsErrorWithoutSaving(Error error)
    {
        _context.Stock.AdjustAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(error);

        var result = await AdjustHandler().Handle(new AdjustStockCommand(OtherShopsProductId, "1", "note", null), CancellationToken.None);

        result.Error.Should().Be(error);
        await _context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adjust_NoUser_ReturnsUnauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        var result = await AdjustHandler().Handle(new AdjustStockCommand(Guid.CreateVersion7(), "1", "note", null), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Count_Changed_ReturnsMovementAndNewQuantity()
    {
        var product = NewProduct();
        var outcome = product.RecordStockCount(26m, UserId, Now).Value;
        _context.Stock.CountAsync(product.Id, null, 26m, UserId, Arg.Any<CancellationToken>())
            .Returns(new StockCountResult(outcome, 26m));

        var result = await CountHandler().Handle(new CountStockCommand(product.Id, "26.000", null), CancellationToken.None);

        result.Value.Changed.Should().BeTrue();
        result.Value.Quantity.Should().Be("26.000");
        result.Value.Movement!.Reason.Should().Be(ContractMovementReason.StockCount);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Count_Unchanged_ReturnsNoMovement()
    {
        _context.Stock.CountAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), 5m, UserId, Arg.Any<CancellationToken>())
            .Returns(new StockCountResult(StockCountOutcome.Unchanged, 5m));

        var result = await CountHandler().Handle(new CountStockCommand(Guid.CreateVersion7(), "5", null), CancellationToken.None);

        result.Value.Changed.Should().BeFalse();
        result.Value.Quantity.Should().Be("5.000");
        result.Value.Movement.Should().BeNull();
    }

    [Theory]
    [InlineData("-1", "Precision.Negative")]
    [InlineData("1.0001", "Precision.TooManyDecimals")]
    public async Task Count_InvalidQuantity_ReturnsPrecisionError(string counted, string code)
    {
        var result = await CountHandler().Handle(new CountStockCommand(Guid.CreateVersion7(), counted, null), CancellationToken.None);

        result.Error.Code.Should().Be(code);
    }

    [Theory]
    [MemberData(nameof(StockErrorsFromTheService))]
    public async Task Count_StockServiceFails_ReturnsItsError(Error error)
    {
        _context.Stock.CountAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<decimal>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(error);

        var result = await CountHandler().Handle(new CountStockCommand(OtherShopsProductId, "1", null), CancellationToken.None);

        result.Error.Should().Be(error);
    }

    public static TheoryData<Error> StockErrorsFromTheService() =>
        new(ProductErrors.NotFound, ProductUnitErrors.NotFound, StockErrors.ZeroChange, StockErrors.NoteRequired);

    private AdjustStockCommandHandler AdjustHandler() => new(_context.CurrentUser, _context.Stock, _context.UnitOfWork);

    private CountStockCommandHandler CountHandler() => new(_context.CurrentUser, _context.Stock, _context.UnitOfWork);
}
