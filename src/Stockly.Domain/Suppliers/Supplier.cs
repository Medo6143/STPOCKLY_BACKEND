using Stockly.Domain.Common;
using Stockly.Domain.Text;

namespace Stockly.Domain.Suppliers;

/// <summary>
/// A shop's supplier. Created on invoice confirmation (Sprint 4) from the name read on the invoice; the scan job only
/// looks suppliers up by <see cref="NormalizedName"/>. Unique per shop among non-archived suppliers.
/// </summary>
public sealed class Supplier : Entity, ITenantEntity
{
    private Supplier(Guid shopId, string name, string? phone, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        Name = name;
        NormalizedName = ArabicTextNormalizer.Normalize(name);
        Phone = phone;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public string? Phone { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Result<Supplier> Create(Guid shopId, string name, string? phone, DateTimeOffset now)
    {
        if (shopId == Guid.Empty)
        {
            return SupplierErrors.ShopRequired;
        }

        if (ArabicTextNormalizer.Normalize(name).Length == 0)
        {
            return SupplierErrors.NameRequired;
        }

        return new Supplier(shopId, name.Trim(), OptionalText.Clean(phone), now);
    }
}
