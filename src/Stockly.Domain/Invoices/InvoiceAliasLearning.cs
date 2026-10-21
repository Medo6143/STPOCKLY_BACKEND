using Stockly.Domain.Common;
using Stockly.Domain.Suppliers;
using Stockly.Domain.Text;

namespace Stockly.Domain.Invoices;

/// <summary>
/// The aliases a confirm learns (docs/design/backend.md §11 step 8): one per normalized raw name; the highest line number
/// decides the product and unit, and every line with the name counts.
/// </summary>
public static class InvoiceAliasLearning
{
    public static Result<List<ProductAlias>> Learn(
        IEnumerable<(InvoiceItem Line, Guid ProductId, Guid UnitId)> received, Guid shopId, Guid? supplierId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(received);

        var learned = new List<ProductAlias>();
        foreach (var group in received.GroupBy(target => ArabicTextNormalizer.Normalize(target.Line.RawName), StringComparer.Ordinal))
        {
            var last = group.MaxBy(target => target.Line.LineNo);
            var alias = ProductAlias.Learn(shopId, last.ProductId, supplierId, last.UnitId, last.Line.RawName, now, group.Count());
            if (alias.IsFailure)
            {
                return alias.Error;
            }

            learned.Add(alias.Value);
        }

        return learned;
    }
}
