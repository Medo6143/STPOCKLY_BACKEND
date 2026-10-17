using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// Order 20: a non-archived product whose normalized name equals the line's. Two or more such products: ambiguous, no
/// match. Unit: the unit with the same normalized name, or the only unit when the line has no unit text (0.90);
/// otherwise the base unit (0.70). One query for all lines.
/// </summary>
public sealed class ExactNameMatchStrategy(IProductRepository products) : IProductMatchStrategy
{
    public int Order => 20;

    public async Task<IReadOnlyDictionary<int, LineMatch>> MatchAsync(
        Guid? supplierId, IReadOnlyList<MatchInput> unmatched, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unmatched);

        var names = unmatched.Select(line => line.NormalizedName).Distinct(StringComparer.Ordinal).ToList();
        var found = await products.FindByNormalizedNamesAsync(names, cancellationToken);
        var byName = found.ToLookup(product => product.NormalizedName, StringComparer.Ordinal);

        var matches = new Dictionary<int, LineMatch>();
        foreach (var line in unmatched)
        {
            var candidates = byName[line.NormalizedName].ToList();
            if (candidates.Count == 1 && Match(line, candidates[0]) is { } match)
            {
                matches[line.LineNo] = match;
            }
        }

        return matches;
    }

    private static LineMatch? Match(MatchInput line, NamedProduct product)
    {
        var unit = line.NormalizedUnit is { } unitText
            ? product.Units.FirstOrDefault(candidate => ArabicTextNormalizer.Normalize(candidate.Name) == unitText)
            : product.Units.Count == 1 ? product.Units[0] : null;
        if (unit is not null)
        {
            return new LineMatch(line.LineNo, product.Id, unit.Id, MatchConfidence.NameAndUnit);
        }

        var baseUnit = product.Units.FirstOrDefault(candidate => candidate.IsBase);
        return baseUnit is null ? null : new LineMatch(line.LineNo, product.Id, baseUnit.Id, MatchConfidence.NameWithBaseUnit);
    }
}
