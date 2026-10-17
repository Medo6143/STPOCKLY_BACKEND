using Stockly.Application.Abstractions.Persistence;

namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// Order 10: the supplier's learned alias (<c>product_aliases</c>, learned on confirm) with exactly this normalized text,
/// for a non-archived product: the alias's unit, confidence 1.00. One query for all lines.
/// </summary>
public sealed class ExactAliasMatchStrategy(IProductAliasRepository aliases) : IProductMatchStrategy
{
    public int Order => 10;

    public async Task<IReadOnlyDictionary<int, LineMatch>> MatchAsync(
        Guid? supplierId, IReadOnlyList<MatchInput> unmatched, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unmatched);

        var names = unmatched.Select(line => line.NormalizedName).Distinct(StringComparer.Ordinal).ToList();
        var found = await aliases.FindExactAsync(supplierId, names, cancellationToken);
        var byAlias = found
            .GroupBy(alias => alias.NormalizedAlias, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var matches = new Dictionary<int, LineMatch>();
        foreach (var line in unmatched)
        {
            if (byAlias.TryGetValue(line.NormalizedName, out var alias))
            {
                matches[line.LineNo] = new LineMatch(line.LineNo, alias.ProductId, alias.ProductUnitId, MatchConfidence.Alias);
            }
        }

        return matches;
    }
}
