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
    /// <summary>Longest product barcode (contract P3).</summary>
    public const int BarcodeMaxLength = 64;

    /// <summary>Longest product category (contract P3).</summary>
    public const int CategoryMaxLength = 100;

    /// <summary>Note of the stock count that records the opening stock of a new product (contract P3).</summary>
    public const string OpeningStockNote = "Opening stock";

    private readonly List<ProductUnit> _units = [];

    private Product(
        Guid shopId, string name, string? barcode, string? category, decimal costPrice, decimal lowStockThreshold, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        Name = name;
        NormalizedName = ArabicTextNormalizer.Normalize(name);
        LatinKey = Text.LatinKey.From(NormalizedName);
        Barcode = barcode;
        Category = category;
        CostPrice = costPrice;
        LowStockThreshold = lowStockThreshold;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    /// <summary>Cross-script matching key of <see cref="NormalizedName"/> (<see cref="Text.LatinKey"/>), kept in step with it.</summary>
    public string LatinKey { get; private set; }

    /// <summary>Trimmed; <c>null</c> when blank. Unique among the shop's non-archived products (database index).</summary>
    public string? Barcode { get; private set; }

    /// <summary>Free text, trimmed; <c>null</c> when blank.</summary>
    public string? Category { get; private set; }

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

    /// <summary>
    /// Creates a product with its base unit (factor 1) and quantity 0. The base unit should be the smallest unit sold.
    /// <paramref name="barcode"/> and <paramref name="category"/> are optional: trimmed, blank becomes <c>null</c>.
    /// </summary>
    public static Result<Product> Create(
        Guid shopId,
        string name,
        string baseUnitName,
        decimal costPrice,
        decimal baseUnitSellPrice,
        decimal lowStockThreshold,
        DateTimeOffset now,
        string? barcode = null,
        string? category = null)
    {
        if (shopId == Guid.Empty)
        {
            return ProductErrors.ShopRequired;
        }

        var cleanBarcode = OptionalText.Clean(barcode);
        var cleanCategory = OptionalText.Clean(category);
        var error = ValidateDetails(name, cleanBarcode, cleanCategory, costPrice, lowStockThreshold);
        if (error is not null)
        {
            return error;
        }

        var product = new Product(shopId, name.Trim(), cleanBarcode, cleanCategory, costPrice, lowStockThreshold, now);

        var baseUnit = ProductUnit.Create(
            shopId, product.Id, baseUnitName, ProductUnit.BaseFactor, baseUnitSellPrice, barcode: null, isBase: true);
        if (baseUnit.IsFailure)
        {
            return baseUnit.Error;
        }

        product._units.Add(baseUnit.Value);
        return product;
    }

    /// <summary>
    /// Replaces the editable details at once (contract P4): every value is checked first, so on failure nothing changes.
    /// The base unit's name and price change through <see cref="UpdateUnit"/>.
    /// </summary>
    public Result Update(string name, string? barcode, string? category, decimal costPrice, decimal lowStockThreshold)
    {
        var cleanBarcode = OptionalText.Clean(barcode);
        var cleanCategory = OptionalText.Clean(category);
        var error = EnsureEditable() ?? ValidateDetails(name, cleanBarcode, cleanCategory, costPrice, lowStockThreshold);
        if (error is not null)
        {
            return error;
        }

        Name = name.Trim();
        NormalizedName = ArabicTextNormalizer.Normalize(Name);
        LatinKey = Text.LatinKey.From(NormalizedName);
        Barcode = cleanBarcode;
        Category = cleanCategory;
        CostPrice = costPrice;
        LowStockThreshold = lowStockThreshold;
        return Result.Success();
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
        LatinKey = Text.LatinKey.From(NormalizedName);
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

    /// <summary>The unit with this id, or the base unit when <paramref name="unitId"/> is <c>null</c>.</summary>
    public Result<ProductUnit> FindUnit(Guid? unitId)
    {
        if (unitId is not { } id)
        {
            return BaseUnit;
        }

        var unit = _units.Find(u => u.Id == id);
        return unit is null ? ProductUnitErrors.NotFound : unit;
    }

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

    /// <summary>
    /// Records a physical count in base units. A count equal to the current quantity creates no movement.
    /// <paramref name="note"/> is optional (e.g. <see cref="OpeningStockNote"/>).
    /// </summary>
    public Result<StockCountOutcome> RecordStockCount(decimal countedQuantity, Guid userId, DateTimeOffset now, string? note = null)
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

        var movement = RecordMovement(change, MovementReason.StockCount, MovementSource.None, userId, note, now);
        return movement.IsSuccess ? new StockCountOutcome(movement.Value) : movement.Error;
    }

    /// <summary>
    /// A manual correction of <paramref name="quantityChange"/> in the given unit (<c>null</c> = base unit), converted
    /// with <see cref="ProductUnit.ToBase"/>. The note is required. Allowed on archived products (late correction).
    /// </summary>
    public Result<StockMovement> AdjustStock(Guid? unitId, decimal quantityChange, Guid userId, string? note, DateTimeOffset now)
    {
        var precisionError = Precision.ValidateQuantity(quantityChange, nameof(quantityChange));
        if (precisionError is not null)
        {
            return precisionError;
        }

        var unit = FindUnit(unitId);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        return RecordMovement(
            unit.Value.ToBase(quantityChange), MovementReason.ManualAdjustment, MovementSource.None, userId, note, now);
    }

    /// <summary>
    /// A physical count of <paramref name="countedQuantity"/> in the given unit (<c>null</c> = base unit), converted
    /// with <see cref="ProductUnit.ToBase"/>. Allowed on archived products (final count).
    /// </summary>
    public Result<StockCountOutcome> CountStock(Guid? unitId, decimal countedQuantity, Guid userId, DateTimeOffset now)
    {
        var precisionError = Precision.ValidateNonNegativeQuantity(countedQuantity, nameof(countedQuantity));
        if (precisionError is not null)
        {
            return precisionError;
        }

        var unit = FindUnit(unitId);
        return unit.IsSuccess
            ? RecordStockCount(unit.Value.ToBase(countedQuantity), userId, now)
            : unit.Error;
    }

    /// <summary>
    /// The opening stock of a new product, in base units: a <see cref="MovementReason.StockCount"/> movement noted
    /// <see cref="OpeningStockNote"/>. Zero records nothing.
    /// </summary>
    public Result<StockCountOutcome> RecordOpeningStock(decimal openingQuantity, Guid userId, DateTimeOffset now) =>
        RecordStockCount(openingQuantity, userId, now, OpeningStockNote);

    private static Error? ValidateName(string name) =>
        ArabicTextNormalizer.Normalize(name).Length == 0 ? ProductErrors.NameRequired : null;

    private static Error? ValidateDetails(
        string name, string? cleanBarcode, string? cleanCategory, decimal costPrice, decimal lowStockThreshold) =>
        ValidateName(name)
        ?? (cleanBarcode?.Length > BarcodeMaxLength ? ProductErrors.BarcodeTooLong : null)
        ?? (cleanCategory?.Length > CategoryMaxLength ? ProductErrors.CategoryTooLong : null)
        ?? Precision.ValidateMoney(costPrice, nameof(costPrice))
        ?? Precision.ValidateNonNegativeQuantity(lowStockThreshold, nameof(lowStockThreshold));

    private Error? EnsureEditable() => IsArchived ? ProductErrors.Archived : null;

    private Error? EnsureUnique(string name, string? barcode, ProductUnit? except)
    {
        var others = _units.Where(u => u != except).ToList();

        var normalizedName = ArabicTextNormalizer.Normalize(name);
        if (others.Exists(u => u.NormalizedName == normalizedName))
        {
            return ProductUnitErrors.DuplicateName;
        }

        var cleanBarcode = OptionalText.Clean(barcode);
        return cleanBarcode is not null && others.Exists(u => u.Barcode == cleanBarcode)
            ? ProductUnitErrors.DuplicateBarcode
            : null;
    }
}
