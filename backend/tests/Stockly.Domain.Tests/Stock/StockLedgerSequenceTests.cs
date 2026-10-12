using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Stock;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Stock;

/// <summary>Quantity and ledger never diverge: every movement's BalanceAfter equals the product quantity right after it.</summary>
public sealed class StockLedgerSequenceTests
{
    private static readonly MovementSource Receipt = MovementSource.FromInvoiceItem(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Sequence_ReceiptSaleCountSaleAdjustment_BalanceAfterTracksQuantity()
    {
        var product = CreateProduct();
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        var ledger = new List<StockMovement>();

        // Receive 10 boxes of 3 strips
        ledger.Add(Record(product, box.ToBase(10m), MovementReason.InvoiceReceipt, Receipt));
        ledger[^1].BalanceAfter.Should().Be(30m);

        // Sell 2.5 strips (fractional base quantity)
        ledger.Add(Record(product, -2.5m, MovementReason.Sale, MovementSource.FromSaleItem(Guid.NewGuid(), Guid.NewGuid())));
        ledger[^1].BalanceAfter.Should().Be(27.5m);

        // Count finds 26 strips
        var count = product.RecordStockCount(26m, UserId, Now).Value;
        count.IsUnchanged.Should().BeFalse();
        ledger.Add(count.Movement!);
        ledger[^1].QuantityChange.Should().Be(-1.5m);
        ledger[^1].Reason.Should().Be(MovementReason.StockCount);
        ledger[^1].BalanceAfter.Should().Be(26m);

        // Sell more than is in stock: negative stock is allowed (docs/erd.md)
        ledger.Add(Record(product, -30m, MovementReason.Sale, MovementSource.FromSaleItem(Guid.NewGuid(), Guid.NewGuid())));
        ledger[^1].BalanceAfter.Should().Be(-4m);
        product.IsLowStock.Should().BeTrue();

        // Manual correction
        ledger.Add(Record(product, 4m, MovementReason.ManualAdjustment, MovementSource.None, "late invoice, counted by hand"));
        ledger[^1].BalanceAfter.Should().Be(0m);

        product.Quantity.Should().Be(0m);
        ledger.Sum(m => m.QuantityChange).Should().Be(product.Quantity, "the ledger is the source of truth (ADR-0001)");
        RunningBalances(ledger).Should().Equal(ledger.Select(m => m.BalanceAfter));
    }

    [Fact]
    public void Sequence_FailedMovementAfterSuccessfulOnes_LeavesQuantityAndLastBalanceUnchanged()
    {
        var product = CreateProduct();
        var receipt = Record(product, 12m, MovementReason.InvoiceReceipt, Receipt);
        var sale = Record(product, -5m, MovementReason.Sale, MovementSource.FromSaleItem(Guid.NewGuid(), Guid.NewGuid()));

        var failures = new[]
        {
            product.RecordMovement(-1m, MovementReason.Sale, MovementSource.None, UserId, null, Now),
            product.RecordMovement(3m, MovementReason.ManualAdjustment, MovementSource.None, UserId, null, Now),
            product.RecordMovement(0.0001m, MovementReason.StockCount, MovementSource.None, UserId, null, Now),
            product.RecordMovement(1m, MovementReason.StockCount, MovementSource.None, Guid.Empty, null, Now),
        };
        var failedCount = product.RecordStockCount(1.0001m, UserId, Now);

        failures.Should().OnlyContain(r => r.IsFailure);
        failedCount.IsFailure.Should().BeTrue();
        product.Quantity.Should().Be(7m);
        sale.BalanceAfter.Should().Be(7m);
        receipt.BalanceAfter.Should().Be(12m);
    }

    [Fact]
    public void RecordStockCount_CountEqualsQuantity_ReturnsUnchangedWithoutMovement()
    {
        var product = CreateProduct();
        Record(product, 8m, MovementReason.InvoiceReceipt, Receipt);

        var result = product.RecordStockCount(8m, UserId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsUnchanged.Should().BeTrue();
        result.Value.Movement.Should().BeNull();
        product.Quantity.Should().Be(8m);
    }

    [Fact]
    public void RecordStockCount_FromNegativeStock_CreatesPositiveCorrection()
    {
        var product = CreateProduct();
        Record(product, -3m, MovementReason.Sale, MovementSource.FromSaleItem(Guid.NewGuid(), Guid.NewGuid()));

        var movement = product.RecordStockCount(5m, UserId, Now).Value.Movement!;

        movement.QuantityChange.Should().Be(8m);
        movement.BalanceAfter.Should().Be(5m);
        product.Quantity.Should().Be(5m);
    }

    [Fact]
    public void RecordStockCount_NegativeCount_ReturnsNegative()
    {
        var result = CreateProduct().RecordStockCount(-1m, UserId, Now);

        result.Error.Should().Be(PrecisionErrors.Negative("countedQuantity"));
    }

    [Fact]
    public void RecordStockCount_CountWithFourDecimals_IsRejected()
    {
        var result = CreateProduct().RecordStockCount(1.0001m, UserId, Now);

        result.Error.Should().Be(PrecisionErrors.TooManyDecimals("countedQuantity", Precision.QuantityDecimals));
    }

    [Fact]
    public void RecordStockCount_EmptyUserEvenWhenUnchanged_ReturnsUserRequired()
    {
        var result = CreateProduct().RecordStockCount(0m, Guid.Empty, Now);

        result.Error.Should().Be(StockErrors.UserRequired);
    }

    private static StockMovement Record(
        Product product, decimal change, MovementReason reason, MovementSource source, string? note = null)
    {
        var result = product.RecordMovement(change, reason, source, UserId, note, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.BalanceAfter.Should().Be(product.Quantity);
        return result.Value;
    }

    private static List<decimal> RunningBalances(IEnumerable<StockMovement> ledger)
    {
        var balance = 0m;
        return ledger.Select(m => balance += m.QuantityChange).ToList();
    }
}
