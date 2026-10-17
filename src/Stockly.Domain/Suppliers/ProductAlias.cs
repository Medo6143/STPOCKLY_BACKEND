using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Suppliers;

/// <summary>
/// "This supplier writes X for product P in unit U", learned when a user confirms an invoice line (Sprint 4). Matching
/// uses it to recognise the same text on the next invoice. Unique per (shop, supplier, normalized alias); the supplier
/// may be unknown (<c>null</c>).
/// </summary>
public sealed class ProductAlias : Entity, ITenantEntity
{
    private ProductAlias(
        Guid shopId, Guid productId, Guid? supplierId, Guid productUnitId, string aliasText, DateTimeOffset lastUsedAt)
    {
        ShopId = shopId;
        ProductId = productId;
        SupplierId = supplierId;
        ProductUnitId = productUnitId;
        AliasText = aliasText;
        NormalizedAlias = ArabicTextNormalizer.Normalize(aliasText);
        TimesConfirmed = 1;
        LastUsedAt = lastUsedAt;
    }

    public Guid ShopId { get; }

    public Guid ProductId { get; private set; }

    public Guid? SupplierId { get; }

    public Guid ProductUnitId { get; private set; }

    public string AliasText { get; }

    public string NormalizedAlias { get; }

    /// <summary>How many confirmed lines taught this alias; at least 1.</summary>
    public int TimesConfirmed { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    /// <summary>The first confirmation of this text.</summary>
    public static Result<ProductAlias> Learn(
        Guid shopId, Guid productId, Guid? supplierId, Guid productUnitId, string aliasText, DateTimeOffset now)
    {
        if (shopId == Guid.Empty)
        {
            return SupplierErrors.ShopRequired;
        }

        if (ArabicTextNormalizer.Normalize(aliasText).Length == 0)
        {
            return SupplierErrors.AliasTextRequired;
        }

        return new ProductAlias(shopId, productId, supplierId, productUnitId, aliasText.Trim(), now);
    }
}
