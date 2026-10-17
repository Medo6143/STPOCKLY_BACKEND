using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Text;

namespace Stockly.Domain.Sales;

/// <summary>
/// A sale recorded on the quick-sale screen (contract S1). <see cref="Start"/> creates the header, so it can be inserted
/// first and serialize retries of the same <see cref="ClientRequestId"/> (ADR-0006, AGENTS.md rules 3 and 4); then
/// <see cref="AddItem"/> adds each line and keeps <see cref="TotalAmount"/> equal to the sum of the line totals. The
/// stock movements are recorded by <c>IStockService</c>, never here (ADR-0001).
/// </summary>
public sealed class Sale : Entity, ITenantEntity
{
    private readonly List<SaleItem> _items = [];

    private Sale(Guid shopId, Guid clientRequestId, Guid createdByUserId, string? note, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        ClientRequestId = clientRequestId;
        CreatedByUserId = createdByUserId;
        Note = note;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    /// <summary>Client-generated id of the sale; unique per shop (ADR-0006).</summary>
    public Guid ClientRequestId { get; }

    public Guid CreatedByUserId { get; }

    /// <summary>Sum of the lines' totals.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Trimmed; <c>null</c> when blank.</summary>
    public string? Note { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>The lines, numbered from 1 in the order they were added.</summary>
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    /// <summary>A new sale without lines and with a total of 0.</summary>
    public static Result<Sale> Start(Guid shopId, Guid clientRequestId, Guid userId, string? note, DateTimeOffset now)
    {
        var cleanNote = OptionalText.Clean(note);
        var error = (shopId == Guid.Empty ? SaleErrors.ShopRequired : null)
            ?? (clientRequestId == Guid.Empty ? SaleErrors.ClientRequestIdRequired : null)
            ?? (userId == Guid.Empty ? SaleErrors.UserRequired : null)
            ?? (cleanNote?.Length > SaleLimits.NoteMaxLength ? SaleErrors.NoteTooLong : null);
        if (error is not null)
        {
            return error;
        }

        return new Sale(shopId, clientRequestId, userId, cleanNote, now);
    }

    /// <summary>
    /// Adds a line of <paramref name="quantity"/> in <paramref name="unit"/> of <paramref name="product"/>. The list price
    /// is the unit's sell price; <paramref name="unitPrice"/> <c>null</c> charges it. An archived product is refused
    /// (<c>Sale.ArchivedProduct</c>), and so is a unit of another product (<c>Sale.UnitNotOfProduct</c>). On failure
    /// nothing changes.
    /// </summary>
    public Result<SaleItem> AddItem(Product product, ProductUnit unit, decimal quantity, decimal? unitPrice)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);

        var error = (_items.Count >= SaleLimits.MaxItems ? SaleErrors.TooManyItems : null)
            ?? (unit.ProductId != product.Id ? SaleErrors.UnitNotOfProduct : null)
            ?? (product.IsArchived ? SaleErrors.ArchivedProduct : null);
        if (error is not null)
        {
            return error;
        }

        var item = SaleItem.Create(
            ShopId, Id, _items.Count + 1, product.Id, unit.Id, quantity, unit.SellPrice, unitPrice ?? unit.SellPrice);
        if (item.IsFailure)
        {
            return item;
        }

        var total = TotalAmount + item.Value.LineTotal;
        var totalError = Precision.ValidateMoney(total, nameof(TotalAmount));
        if (totalError is not null)
        {
            return totalError;
        }

        _items.Add(item.Value);
        TotalAmount = total;
        return item;
    }
}
