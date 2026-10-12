using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Stock;

/// <summary>Adjustments and counts in any unit of the product (contract P9, P10), and opening stock (P3).</summary>
public sealed class StockInUnitsTests
{
    [Fact]
    public void AdjustStock_BaseUnit_RecordsManualAdjustmentWithNote()
    {
        var product = CreateProduct();

        var movement = product.AdjustStock(null, 5m, UserId, " Found in the back ", Now).Value;

        movement.Reason.Should().Be(MovementReason.ManualAdjustment);
        movement.QuantityChange.Should().Be(5m);
        movement.BalanceAfter.Should().Be(5m);
        movement.Note.Should().Be("Found in the back");
        product.Quantity.Should().Be(5m);
    }

    [Fact]
    public void AdjustStock_OtherUnit_ConvertsToBaseUnits()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;

        var movement = product.AdjustStock(box.Id, -2m, UserId, "Expired", Now).Value;

        movement.QuantityChange.Should().Be(-6m);
        product.Quantity.Should().Be(-6m);
    }

    [Fact]
    public void AdjustStock_FractionalFactor_RoundsAwayFromZeroToThreeDecimals()
    {
        var product = CreateProduct();
        var third = product.AddUnit("ثلث", 0.333333m, 5m, null).Value;

        var movement = product.AdjustStock(third.Id, 1.5m, UserId, "note", Now).Value;

        movement.QuantityChange.Should().Be(0.5m);
    }

    [Fact]
    public void AdjustStock_UnknownUnit_ReturnsUnitNotFoundAndChangesNothing()
    {
        var product = CreateProduct();

        var result = product.AdjustStock(Guid.NewGuid(), 1m, UserId, "note", Now);

        result.Error.Should().Be(ProductUnitErrors.NotFound);
        product.Quantity.Should().Be(0m);
    }

    [Fact]
    public void AdjustStock_Zero_ReturnsZeroChange()
    {
        CreateProduct().AdjustStock(null, 0m, UserId, "note", Now).Error.Should().Be(StockErrors.ZeroChange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AdjustStock_BlankNote_ReturnsNoteRequired(string? note)
    {
        CreateProduct().AdjustStock(null, 1m, UserId, note, Now).Error.Should().Be(StockErrors.NoteRequired);
    }

    [Fact]
    public void AdjustStock_TooManyDecimalsInTheUnit_IsRejectedBeforeConversion()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 2m, 40m, null).Value;

        var result = product.AdjustStock(box.Id, 0.0005m, UserId, "note", Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("quantityChange", Precision.QuantityDecimals));
    }

    [Fact]
    public void AdjustStock_ArchivedProduct_IsAllowed()
    {
        var product = CreateProduct();
        product.Archive();

        product.AdjustStock(null, -1m, UserId, "late correction", Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CountStock_OtherUnit_RecordsDifferenceInBaseUnits()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        product.AdjustStock(null, 4m, UserId, "start", Now);

        var outcome = product.CountStock(box.Id, 2m, UserId, Now).Value;

        outcome.Movement!.QuantityChange.Should().Be(2m);
        outcome.Movement.Reason.Should().Be(MovementReason.StockCount);
        outcome.Movement.Note.Should().BeNull();
        product.Quantity.Should().Be(6m);
    }

    [Fact]
    public void CountStock_EqualToQuantity_IsUnchanged()
    {
        var product = CreateProduct();

        product.CountStock(null, 0m, UserId, Now).Value.IsUnchanged.Should().BeTrue();
    }

    [Fact]
    public void CountStock_UnknownUnit_ReturnsUnitNotFound()
    {
        CreateProduct().CountStock(Guid.NewGuid(), 1m, UserId, Now).Error.Should().Be(ProductUnitErrors.NotFound);
    }

    [Fact]
    public void CountStock_Negative_ReturnsNegative()
    {
        CreateProduct().CountStock(null, -1m, UserId, Now).Error.Should().Be(PrecisionErrors.Negative("countedQuantity"));
    }

    [Fact]
    public void CountStock_ArchivedProduct_IsAllowed()
    {
        var product = CreateProduct();
        product.Archive();

        product.CountStock(null, 3m, UserId, Now).Value.IsUnchanged.Should().BeFalse();
    }

    [Fact]
    public void RecordStockCount_WithNote_KeepsTrimmedNote()
    {
        var product = CreateProduct();

        var movement = product.RecordStockCount(3m, UserId, Now, "  shelf recount ").Value.Movement!;

        movement.Note.Should().Be("shelf recount");
    }

    [Fact]
    public void RecordOpeningStock_Positive_RecordsStockCountWithOpeningNote()
    {
        var product = CreateProduct();

        var movement = product.RecordOpeningStock(24m, UserId, Now).Value.Movement!;

        movement.Reason.Should().Be(MovementReason.StockCount);
        movement.Note.Should().Be(Product.OpeningStockNote);
        movement.QuantityChange.Should().Be(24m);
        movement.BalanceAfter.Should().Be(24m);
        product.Quantity.Should().Be(24m);
    }

    [Fact]
    public void RecordOpeningStock_Zero_RecordsNothing()
    {
        var product = CreateProduct();

        product.RecordOpeningStock(0m, UserId, Now).Value.IsUnchanged.Should().BeTrue();
        product.Quantity.Should().Be(0m);
    }

    [Fact]
    public void RecordOpeningStock_Negative_ReturnsNegative()
    {
        CreateProduct().RecordOpeningStock(-1m, UserId, Now).Error.Should().Be(PrecisionErrors.Negative("countedQuantity"));
    }
}
