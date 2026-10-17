namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// Runs the strategies by order; each one only sees the lines the earlier ones left unmatched and is not called when
/// nothing is left.
/// </summary>
public sealed class ProductMatcher(IEnumerable<IProductMatchStrategy> strategies) : IProductMatcher
{
    private readonly IProductMatchStrategy[] _strategies = [.. strategies.OrderBy(strategy => strategy.Order)];

    public async Task<IReadOnlyList<LineMatch>> MatchAsync(
        Guid? supplierId, IReadOnlyList<MatchInput> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var matched = new Dictionary<int, LineMatch>();
        foreach (var strategy in _strategies)
        {
            var unmatched = lines.Where(line => !matched.ContainsKey(line.LineNo)).ToList();
            if (unmatched.Count == 0)
            {
                break;
            }

            var found = await strategy.MatchAsync(supplierId, unmatched, cancellationToken);
            foreach (var line in unmatched)
            {
                if (found.TryGetValue(line.LineNo, out var match) && match.IsMatched)
                {
                    matched[line.LineNo] = match;
                }
            }
        }

        return [.. lines.Select(line => matched.TryGetValue(line.LineNo, out var match) ? match : LineMatch.None(line.LineNo))];
    }
}
