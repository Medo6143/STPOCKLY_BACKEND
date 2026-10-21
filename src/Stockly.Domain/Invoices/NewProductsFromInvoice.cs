using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Text;

namespace Stockly.Domain.Invoices;

/// <summary>
/// The products a confirm creates (docs/design/backend.md §10.5): lines with the same normalized name and base unit
/// share one product, and the lowest line number sets its prices. Pure: the caller adds the products to the repository.
/// </summary>
public static class NewProductsFromInvoice
{
    public static Result<IReadOnlyDictionary<(string Name, string BaseUnit), Product>> Create(
        IEnumerable<InvoiceItem> lines, Guid shopId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var created = new Dictionary<(string Name, string BaseUnit), Product>();
        foreach (var line in lines.Where(line => line.NewProductName is not null).OrderBy(line => line.LineNo))
        {
            var key = KeyOf(line);
            if (created.ContainsKey(key))
            {
                continue;
            }

            var product = Product.Create(
                shopId,
                line.NewProductName!,
                line.NewProductBaseUnitName!,
                costPrice: line.UnitPrice,
                baseUnitSellPrice: line.NewProductSellPrice!.Value,
                lowStockThreshold: 0m,
                now);
            if (product.IsFailure)
            {
                return product.Error;
            }

            created[key] = product.Value;
        }

        return created;
    }

    /// <summary>The product a new-product line belongs to: its normalized name and base unit.</summary>
    public static (string Name, string BaseUnit) KeyOf(InvoiceItem line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return (ArabicTextNormalizer.Normalize(line.NewProductName!), ArabicTextNormalizer.Normalize(line.NewProductBaseUnitName!));
    }
}
