using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// Order 30: fuzzy match with <c>pg_trgm</c> on the lines the exact strategies left (docs/features/invoice-scan.md). Each
/// line is compared, by its normalized text and by its <see cref="LatinKey"/> (cross-script: "بنادول اكسترا" ↔
/// "Panadol Extra"), with the shop's non-archived product names and with the aliases learned from every supplier; the
/// higher similarity counts and must reach <see cref="MatchingOptions.TrigramThreshold"/>. One query for all lines.
/// <para>
/// Confidence is the similarity rounded to 2 decimals, never above what the exact strategy of the same kind gives: an
/// alias of the invoice's supplier up to 1.00, an alias learned from another supplier × 0.9, a product name up to 0.90
/// with a certain unit and 0.70 with the base unit (unit rules of <see cref="ExactNameMatchStrategy"/>). The best
/// confidence wins; two different products sharing it are ambiguous and the line stays unmatched.
/// </para>
/// </summary>
public sealed class TrigramMatchStrategy(IProductSimilarityQueries similarity, MatchingOptions options) : IProductMatchStrategy
{
    /// <summary>Factor applied to an alias learned from another supplier.</summary>
    public const decimal OtherSupplierFactor = 0.9m;

    public int Order => 30;

    public async Task<IReadOnlyDictionary<int, LineMatch>> MatchAsync(
        Guid? supplierId, IReadOnlyList<MatchInput> unmatched, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unmatched);

        var probes = unmatched
            .Where(line => line.NormalizedName.Length > 0)
            .Select(line => new SimilarityProbe(line.LineNo, line.NormalizedName, LatinKey.From(line.NormalizedName)))
            .ToList();
        if (probes.Count == 0)
        {
            return new Dictionary<int, LineMatch>();
        }

        var candidates = await similarity.FindSimilarAsync(supplierId, probes, options.TrigramThreshold, cancellationToken);
        var byLine = candidates.ToLookup(candidate => candidate.LineNo);

        var matches = new Dictionary<int, LineMatch>();
        foreach (var line in unmatched)
        {
            if (Best(line, byLine[line.LineNo]) is { } match)
            {
                matches[line.LineNo] = match;
            }
        }

        return matches;
    }

    private static LineMatch? Best(MatchInput line, IEnumerable<SimilarCandidate> candidates)
    {
        // On equal confidence an alias (a unit a person confirmed) comes before a name match of the same product.
        var scored = candidates
            .Select(candidate => (Match: Score(line, candidate), FromAlias: candidate.AliasUnitId is not null))
            .Where(scoredCandidate => scoredCandidate.Match is not null)
            .OrderByDescending(scoredCandidate => scoredCandidate.Match!.Confidence)
            .ThenByDescending(scoredCandidate => scoredCandidate.FromAlias)
            .Select(scoredCandidate => scoredCandidate.Match!)
            .ToList();
        if (scored.Count == 0)
        {
            return null;
        }

        var best = scored[0];
        var ambiguous = scored.Exists(match => match.Confidence == best.Confidence && match.ProductId != best.ProductId);
        return ambiguous ? null : best;
    }

    private static LineMatch? Score(MatchInput line, SimilarCandidate candidate)
    {
        var similarity = Math.Round((decimal)candidate.Similarity, 2, MidpointRounding.AwayFromZero);
        if (candidate.AliasUnitId is { } aliasUnit)
        {
            var confidence = candidate.OtherSupplier
                ? Math.Round(similarity * OtherSupplierFactor, 2, MidpointRounding.AwayFromZero)
                : similarity;
            return new LineMatch(line.LineNo, candidate.ProductId, aliasUnit, Math.Min(confidence, MatchConfidence.Alias));
        }

        var unit = ExactNameMatchStrategy.CertainUnit(line, candidate.Units);
        if (unit is not null)
        {
            return new LineMatch(line.LineNo, candidate.ProductId, unit.Id, Math.Min(similarity, MatchConfidence.NameAndUnit));
        }

        var baseUnit = candidate.Units.FirstOrDefault(candidateUnit => candidateUnit.IsBase);
        return baseUnit is null
            ? null
            : new LineMatch(line.LineNo, candidate.ProductId, baseUnit.Id, Math.Min(similarity, MatchConfidence.NameWithBaseUnit));
    }
}
