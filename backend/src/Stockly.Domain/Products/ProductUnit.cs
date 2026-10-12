using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Products;

/// <summary>
/// A unit a product is bought or sold in (ADR-0003). <see cref="Factor"/> is how many base units one of this
/// unit holds. Created and changed only through <see cref="Product"/>, which enforces uniqueness rules.
/// </summary>
public sealed class ProductUnit : Entity, ITenantEntity
{
    internal const decimal BaseFactor = 1m;

    private ProductUnit(Guid shopId, Guid productId, string name, decimal factor, decimal sellPrice, string? barcode, bool isBase)
    {
        ShopId = shopId;
        ProductId = productId;
        Name = name;
        Factor = factor;
        SellPrice = sellPrice;
        Barcode = barcode;
        IsBase = isBase;
    }

    public Guid ShopId { get; }

    public Guid ProductId { get; }

    public string Name { get; private set; }

    public decimal Factor { get; private set; }

    public decimal SellPrice { get; private set; }

    public string? Barcode { get; private set; }

    public bool IsBase { get; }

    internal string NormalizedName => ArabicTextNormalizer.Normalize(Name);

    /// <summary>Converts a quantity in this unit to base units, rounded to 3 decimals away from zero.</summary>
    public decimal ToBase(decimal quantity) =>
        decimal.Round(quantity * Factor, Precision.QuantityDecimals, MidpointRounding.AwayFromZero);

    internal static Result<ProductUnit> Create(
        Guid shopId, Guid productId, string name, decimal factor, decimal sellPrice, string? barcode, bool isBase)
    {
        var error = Validate(name, factor, sellPrice);
        if (error is not null)
        {
            return error;
        }

        return new ProductUnit(shopId, productId, name.Trim(), factor, sellPrice, CleanBarcode(barcode), isBase);
    }

    internal static Error? Validate(string name, decimal factor, decimal sellPrice)
    {
        if (ArabicTextNormalizer.Normalize(name).Length == 0)
        {
            return ProductUnitErrors.NameRequired;
        }

        return Precision.ValidateFactor(factor, nameof(factor))
            ?? Precision.ValidateMoney(sellPrice, nameof(sellPrice));
    }

    internal static string? CleanBarcode(string? barcode) =>
        string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    /// <summary>Applies already-validated values (see <see cref="Validate"/>).</summary>
    internal void Apply(string name, decimal factor, decimal sellPrice, string? barcode)
    {
        Name = name.Trim();
        Factor = factor;
        SellPrice = sellPrice;
        Barcode = CleanBarcode(barcode);
    }
}
