using Stockly.Domain.Common;
using Stockly.Domain.Stock;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Stock;

public sealed class StockMovementRulesTests
{
    private static readonly Guid InvoiceId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a1");
    private static readonly Guid InvoiceItemId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a2");
    private static readonly Guid SaleId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");
    private static readonly Guid SaleItemId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b2");

    private static MovementSource InvoiceSource => MovementSource.FromInvoiceItem(InvoiceId, InvoiceItemId);

    private static MovementSource SaleSource => MovementSource.FromSaleItem(SaleId, SaleItemId);

    public static TheoryData<MovementReason, decimal, MovementSource, string?> ValidMovements => new()
    {
        { MovementReason.InvoiceReceipt, 10m, InvoiceSource, null },
        { MovementReason.Sale, -2m, SaleSource, null },
        { MovementReason.ManualAdjustment, 3m, MovementSource.None, "found in back room" },
        { MovementReason.ManualAdjustment, -1m, MovementSource.None, "broken" },
        { MovementReason.StockCount, 4m, MovementSource.None, null },
        { MovementReason.StockCount, -4m, MovementSource.None, "count note" },
        { MovementReason.InvoiceReversal, -10m, InvoiceSource, null },
    };

    public static TheoryData<MovementReason, decimal, MovementSource, string?, string> InvalidMovements => new()
    {
        // InvoiceReceipt: invoice source + positive change
        { MovementReason.InvoiceReceipt, -1m, InvoiceSource, null, nameof(StockErrors.ChangeMustBePositive) },
        { MovementReason.InvoiceReceipt, 1m, MovementSource.None, null, nameof(StockErrors.InvoiceSourceRequired) },
        { MovementReason.InvoiceReceipt, 1m, SaleSource, null, nameof(StockErrors.InvoiceSourceRequired) },
        // Sale: sale source + negative change
        { MovementReason.Sale, 1m, SaleSource, null, nameof(StockErrors.ChangeMustBeNegative) },
        { MovementReason.Sale, -1m, MovementSource.None, null, nameof(StockErrors.SaleSourceRequired) },
        { MovementReason.Sale, -1m, InvoiceSource, null, nameof(StockErrors.SaleSourceRequired) },
        // ManualAdjustment: no source + note
        { MovementReason.ManualAdjustment, 1m, MovementSource.None, null, nameof(StockErrors.NoteRequired) },
        { MovementReason.ManualAdjustment, 1m, MovementSource.None, "   ", nameof(StockErrors.NoteRequired) },
        { MovementReason.ManualAdjustment, 1m, InvoiceSource, "note", nameof(StockErrors.SourceNotAllowed) },
        // StockCount: no source
        { MovementReason.StockCount, 1m, SaleSource, null, nameof(StockErrors.SourceNotAllowed) },
        // InvoiceReversal: invoice source + negative change
        { MovementReason.InvoiceReversal, 1m, InvoiceSource, null, nameof(StockErrors.ChangeMustBeNegative) },
        { MovementReason.InvoiceReversal, -1m, MovementSource.None, null, nameof(StockErrors.InvoiceSourceRequired) },
        // Empty references are never valid
        { MovementReason.InvoiceReceipt, 1m, MovementSource.FromInvoiceItem(Guid.Empty, InvoiceItemId), null, nameof(StockErrors.EmptySourceReference) },
        { MovementReason.InvoiceReceipt, 1m, MovementSource.FromInvoiceItem(InvoiceId, Guid.Empty), null, nameof(StockErrors.EmptySourceReference) },
        { MovementReason.Sale, -1m, MovementSource.FromSaleItem(Guid.Empty, SaleItemId), null, nameof(StockErrors.EmptySourceReference) },
        { MovementReason.Sale, -1m, MovementSource.FromSaleItem(SaleId, Guid.Empty), null, nameof(StockErrors.EmptySourceReference) },
    };

    [Theory]
    [MemberData(nameof(ValidMovements))]
    public void RecordMovement_ValidForReason_CreatesMovement(MovementReason reason, decimal change, MovementSource source, string? note)
    {
        var product = CreateProduct();

        var result = product.RecordMovement(change, reason, source, UserId, note, Now);

        result.IsSuccess.Should().BeTrue();
        var movement = result.Value;
        movement.ShopId.Should().Be(ShopId);
        movement.ProductId.Should().Be(product.Id);
        movement.Reason.Should().Be(reason);
        movement.QuantityChange.Should().Be(change);
        movement.BalanceAfter.Should().Be(change);
        movement.InvoiceId.Should().Be(source.InvoiceId);
        movement.InvoiceItemId.Should().Be(source.InvoiceItemId);
        movement.SaleId.Should().Be(source.SaleId);
        movement.SaleItemId.Should().Be(source.SaleItemId);
        movement.CreatedByUserId.Should().Be(UserId);
        movement.CreatedAt.Should().Be(Now);
        movement.Id.Should().NotBe(Guid.Empty);
        product.Quantity.Should().Be(change);
    }

    [Theory]
    [MemberData(nameof(InvalidMovements))]
    public void RecordMovement_BreaksReasonRule_ReturnsErrorAndKeepsQuantity(
        MovementReason reason, decimal change, MovementSource source, string? note, string expectedError)
    {
        var product = CreateProduct();

        var result = product.RecordMovement(change, reason, source, UserId, note, Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"Stock.{expectedError}");
        result.Error.Type.Should().Be(ErrorType.Validation);
        product.Quantity.Should().Be(0m);
    }

    [Fact]
    public void RecordMovement_ZeroChange_ReturnsZeroChange()
    {
        var result = CreateProduct().RecordMovement(0m, MovementReason.StockCount, MovementSource.None, UserId, null, Now);

        result.Error.Should().Be(StockErrors.ZeroChange);
    }

    [Fact]
    public void RecordMovement_EmptyUser_ReturnsUserRequired()
    {
        var result = CreateProduct().RecordMovement(1m, MovementReason.StockCount, MovementSource.None, Guid.Empty, null, Now);

        result.Error.Should().Be(StockErrors.UserRequired);
    }

    [Fact]
    public void RecordMovement_UndefinedReason_ReturnsUnknownReason()
    {
        var result = CreateProduct().RecordMovement(1m, (MovementReason)99, MovementSource.None, UserId, null, Now);

        result.Error.Should().Be(StockErrors.UnknownReason);
    }

    [Fact]
    public void RecordMovement_ChangeWithFourDecimals_IsRejectedNotRounded()
    {
        var result = CreateProduct().RecordMovement(1.0005m, MovementReason.StockCount, MovementSource.None, UserId, null, Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("quantityChange", Precision.QuantityDecimals));
    }

    [Fact]
    public void RecordMovement_BalanceWouldNotFitColumn_ReturnsOutOfRange()
    {
        var product = CreateProduct();
        product.RecordMovement(99_999_999_999m, MovementReason.StockCount, MovementSource.None, UserId, null, Now)
            .IsSuccess.Should().BeTrue();

        var result = product.RecordMovement(1m, MovementReason.StockCount, MovementSource.None, UserId, null, Now);

        result.Error.Should().Be(PrecisionErrors.OutOfRange("balanceAfter"));
        product.Quantity.Should().Be(99_999_999_999m);
    }

    [Fact]
    public void RecordMovement_NoteIsTrimmed_BlankNoteStoredAsNull()
    {
        var product = CreateProduct();

        var withNote = product.RecordMovement(1m, MovementReason.ManualAdjustment, MovementSource.None, UserId, "  found  ", Now).Value;
        var blankNote = product.RecordMovement(1m, MovementReason.StockCount, MovementSource.None, UserId, "  ", Now).Value;

        withNote.Note.Should().Be("found");
        blankNote.Note.Should().BeNull();
    }

    [Fact]
    public void MovementSource_Factories_SetExactlyOneSourceKind()
    {
        MovementSource.None.Should().BeEquivalentTo(new { InvoiceId = (Guid?)null, InvoiceItemId = (Guid?)null, SaleId = (Guid?)null, SaleItemId = (Guid?)null });
        InvoiceSource.Should().BeEquivalentTo(new { InvoiceId = (Guid?)InvoiceId, InvoiceItemId = (Guid?)InvoiceItemId, SaleId = (Guid?)null, SaleItemId = (Guid?)null });
        SaleSource.Should().BeEquivalentTo(new { InvoiceId = (Guid?)null, InvoiceItemId = (Guid?)null, SaleId = (Guid?)SaleId, SaleItemId = (Guid?)SaleItemId });
    }

    [Fact]
    public void MovementReason_Values_AreExplicitAndStable()
    {
        ((int)MovementReason.InvoiceReceipt).Should().Be(1);
        ((int)MovementReason.Sale).Should().Be(2);
        ((int)MovementReason.ManualAdjustment).Should().Be(3);
        ((int)MovementReason.StockCount).Should().Be(4);
        ((int)MovementReason.InvoiceReversal).Should().Be(5);
    }
}
