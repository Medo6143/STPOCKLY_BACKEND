using Stockly.Domain.Common;

namespace Stockly.Domain.Stock;

/// <summary>
/// One append-only row of the stock ledger (ADR-0001). Created only by <c>Product.RecordMovement</c>,
/// which sets the product quantity to <see cref="BalanceAfter"/> in the same step.
/// </summary>
public sealed class StockMovement : Entity, ITenantEntity
{
    // For EF materialization only; the real constructor takes a MovementSource, which is not a mapped property.
    private StockMovement()
    {
    }

    private StockMovement(
        Guid shopId,
        Guid productId,
        decimal quantityChange,
        decimal balanceAfter,
        MovementReason reason,
        MovementSource source,
        string? note,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        ShopId = shopId;
        ProductId = productId;
        QuantityChange = quantityChange;
        BalanceAfter = balanceAfter;
        Reason = reason;
        InvoiceId = source.InvoiceId;
        InvoiceItemId = source.InvoiceItemId;
        SaleId = source.SaleId;
        SaleItemId = source.SaleItemId;
        Note = note;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public Guid ProductId { get; }

    /// <summary>Change in the product's base unit. Never 0.</summary>
    public decimal QuantityChange { get; }

    /// <summary>Product quantity right after this movement.</summary>
    public decimal BalanceAfter { get; }

    public MovementReason Reason { get; }

    public Guid? InvoiceId { get; }

    public Guid? InvoiceItemId { get; }

    public Guid? SaleId { get; }

    public Guid? SaleItemId { get; }

    public string? Note { get; }

    public Guid CreatedByUserId { get; }

    public DateTimeOffset CreatedAt { get; }

    internal static Result<StockMovement> Create(
        Guid shopId,
        Guid productId,
        decimal quantityChange,
        decimal balanceAfter,
        MovementReason reason,
        MovementSource source,
        Guid userId,
        string? note,
        DateTimeOffset now)
    {
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        var error = Validate(quantityChange, reason, source, userId, trimmedNote);
        if (error is not null)
        {
            return error;
        }

        return new StockMovement(shopId, productId, quantityChange, balanceAfter, reason, source, trimmedNote, userId, now);
    }

    private static Error? Validate(decimal change, MovementReason reason, MovementSource source, Guid userId, string? note)
    {
        if (change == 0m)
        {
            return StockErrors.ZeroChange;
        }

        if (userId == Guid.Empty)
        {
            return StockErrors.UserRequired;
        }

        if (source.HasEmptyReference)
        {
            return StockErrors.EmptySourceReference;
        }

        return reason switch
        {
            MovementReason.InvoiceReceipt => RequireInvoiceLine(source) ?? RequireIncrease(change),
            MovementReason.InvoiceReversal => RequireInvoiceLine(source) ?? RequireDecrease(change),
            MovementReason.Sale => RequireSaleLine(source) ?? RequireDecrease(change),
            MovementReason.ManualAdjustment => RequireNoSource(source) ?? RequireNote(note),
            MovementReason.StockCount => RequireNoSource(source),
            _ => StockErrors.UnknownReason,
        };
    }

    private static Error? RequireInvoiceLine(MovementSource source) =>
        source.IsInvoiceItem ? null : StockErrors.InvoiceSourceRequired;

    private static Error? RequireSaleLine(MovementSource source) =>
        source.IsSaleItem ? null : StockErrors.SaleSourceRequired;

    private static Error? RequireNoSource(MovementSource source) =>
        source.IsNone ? null : StockErrors.SourceNotAllowed;

    private static Error? RequireNote(string? note) =>
        note is null ? StockErrors.NoteRequired : null;

    private static Error? RequireIncrease(decimal change) =>
        change > 0m ? null : StockErrors.ChangeMustBePositive;

    private static Error? RequireDecrease(decimal change) =>
        change < 0m ? null : StockErrors.ChangeMustBeNegative;
}
