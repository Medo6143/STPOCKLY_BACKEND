using Stockly.Domain.Common;
using Stockly.Domain.Stock;
using Stockly.Domain.Text;

namespace Stockly.Domain.Products;

/// <summary>
/// Aggregate root for a shop's product and its units. <see cref="Quantity"/> is a projection of the stock
/// ledger (ADR-0001): it changes only through <see cref="RecordMovement"/>, which returns the matching
/// <see cref="StockMovement"/>, so quantity and ledger cannot diverge.
/// </summary>
public sealed class Product : Entity, ITenantEntity
{
    private readonly List<ProductUnit> _units = [];

    private Product(Guid shopId, string name, decimal costPrice, decimal lowStockThreshold, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        Name = name;
        NormalizedName = ArabicTextNormalizer.Normalize(name);
        CostPrice = costPrice;
        LowStockThreshold = lowStockThreshold;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    /// <summary>Stock in the base unit. May be negative (a sale recorded before its invoice).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Cost per base unit.</summary>
    public decimal CostPrice { get; private set; }

    /// <summary>In base units. With the default 0, out-of-stock products are low stock.</summary>
    public decimal LowStockThreshold { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<ProductUnit> Units => _units.AsReadOnly();

    public ProductUnit BaseUnit => _units.Single(unit => unit.IsBase);

    public bool IsLowStock => Quantity <= LowStockThreshold;

    /// <summary>Creates a product with its base unit (factor 1). The base unit should be the smallest unit sold.</summary>
    public static Result<Product> Create(
        Guid shopId,
        string name,
        string baseUnitName,
        decimal costPrice,
        decimal baseUnitSellPrice,
        decimal lowStockThreshold,
        DateTimeOffset now)
    {
        if (shopId == Guid.Empty)
        {
            return ProductErrors.ShopRequired;
        }

        var error = ValidateName(name)
            ?? Precision.ValidateMoney(costPrice, nameof(costPrice))
            ?? Precision.ValidateNonNegativeQuantity(lowStockThreshold, nameof(lowStockThreshold));
        if (error is not null)
        {
            return error;
        }

        var product = new Product(shopId, name.Trim(), costPrice, lowStockThreshold, now);

        var baseUnit = ProductUnit.Create(
            shopId, product.Id, baseUnitName, ProductUnit.BaseFactor, baseUnitSellPrice, barcode: null, isBase: true);
        if (baseUnit.IsFailure)
        {
            return baseUnit.Error;
        }

        product._units.Add(baseUnit.Value);
        return product;
    }

    public Result Rename(string name)
    {
        var error = EnsureEditable() ?? ValidateName(name);
        if (error is not null)
        {
            return error;
        }

        Name = name.Trim();
        NormalizedName = ArabicTextNormalizer.Normalize(Name);
        return Result.Success();
    }

    public Result ChangeCostPrice(decimal costPrice)
    {
        var error = EnsureEditable() ?? Precision.ValidateMoney(costPrice, nameof(costPrice));
        if (error is not null)
        {
            return error;
        }

        CostPrice = costPrice;
        return Result.Success();
    }

    public Result SetLowStockThreshold(decimal lowStockThreshold)
    {
        var error = EnsureEditable() ?? Precision.ValidateNonNegativeQuantity(lowStockThreshold, nameof(lowStockThreshold));
        if (error is not null)
        {
            return error;
        }

        LowStockThreshold = lowStockThreshold;
        return Result.Success();
    }

    /// <summary>Soft delete. Idempotent. Stock movements stay allowed (late sales, final count).</summary>
    public void Archive() => IsArchived = true;

    public Result<ProductUnit> AddUnit(string name, decimal factor, decimal sellPrice, string? barcode)
    {
        var error = EnsureEditable() ?? EnsureUnique(name, barcode, except: null);
        if (error is not null)
        {
            return error;
        }

        var unit = ProductUnit.Create(ShopId, Id, name, factor, sellPrice, barcode, isBase: false);
        if (unit.IsSuccess)
        {
            _units.Add(unit.Value);
        }

        return unit;
    }

    public Result UpdateUnit(Guid unitId, string name, decimal factor, decimal sellPrice, string? barcode)
    {
        var editable = EnsureEditable();
        if (editable is not null)
        {
            return editable;
        }

        var unit = _units.Find(u => u.Id == unitId);
        if (unit is null)
        {
            return ProductUnitErrors.NotFound;
        }

        var error = (unit.IsBase && factor != ProductUnit.BaseFactor ? ProductUnitErrors.BaseUnitFactorFixed : null)
            ?? ProductUnit.Validate(name, factor, sellPrice)
            ?? EnsureUnique(name, barcode, except: unit);
        if (error is not null)
        {
            return error;
        }

        unit.Apply(name, factor, sellPrice, barcode);
        return Result.Success();
    }

    public Result RemoveUnit(Guid unitId)
    {
        var editable = EnsureEditable();
        if (editable is not null)
        {
            return editable;
        }

        var unit = _units.Find(u => u.Id == unitId);
        if (unit is null)
        {
            return ProductUnitErrors.NotFound;
        }

        if (unit.IsBase)
        {
            return ProductUnitErrors.BaseUnitCannotBeRemoved;
        }

        _units.Remove(unit);
        return Result.Success();
    }

    /// <summary>
    /// The only way to change <see cref="Quantity"/>. Validates the movement first; on success sets the
    /// quantity to the movement's <see cref="StockMovement.BalanceAfter"/>. On failure nothing changes.
    /// <c>IStockService</c> calls this inside the database transaction.
    /// </summary>
    public Result<StockMovement> RecordMovement(
        decimal quantityChange,
        MovementReason reason,
        MovementSource source,
        Guid userId,
        string? note,
        DateTimeOffset now)
    {
        var precisionError = Precision.ValidateQuantity(quantityChange, nameof(quantityChange));
        if (precisionError is not null)
        {
            return precisionError;
        }

        var balanceAfter = Quantity + quantityChange;

        var movement = StockMovement.Create(ShopId, Id, quantityChange, balanceAfter, reason, source, userId, note, now);
        if (movement.IsFailure)
        {
            return movement;
        }

        var balanceError = Precision.ValidateQuantity(balanceAfter, nameof(balanceAfter));
        if (balanceError is not null)
        {
            return balanceError;
        }

        Quantity = balanceAfter;
        return movement;
    }

    /// <summary>Records a physical count. A count equal to the current quantity creates no movement.</summary>
    public Result<StockCountOutcome> RecordStockCount(decimal countedQuantity, Guid userId, DateTimeOffset now)
    {
        var error = Precision.ValidateNonNegativeQuantity(countedQuantity, nameof(countedQuantity))
            ?? (userId == Guid.Empty ? StockErrors.UserRequired : null);
        if (error is not null)
        {
            return error;
        }

        var change = countedQuantity - Quantity;
        if (change == 0m)
        {
            return StockCountOutcome.Unchanged;
        }

        var movement = RecordMovement(change, MovementReason.StockCount, MovementSource.None, userId, note: null, now);
        return movement.IsSuccess ? new StockCountOutcome(movement.Value) : movement.Error;
    }

    private static Error? ValidateName(string name) =>
        ArabicTextNormalizer.Normalize(name).Length == 0 ? ProductErrors.NameRequired : null;

    private Error? EnsureEditable() => IsArchived ? ProductErrors.Archived : null;

    private Error? EnsureUnique(string name, string? barcode, ProductUnit? except)
    {
        var others = _units.Where(u => u != except).ToList();

        var normalizedName = ArabicTextNormalizer.Normalize(name);
        if (others.Exists(u => u.NormalizedName == normalizedName))
        {
            return ProductUnitErrors.DuplicateName;
        }

        var cleanBarcode = ProductUnit.CleanBarcode(barcode);
        return cleanBarcode is not null && others.Exists(u => u.Barcode == cleanBarcode)
            ? ProductUnitErrors.DuplicateBarcode
            : null;
    }
}
