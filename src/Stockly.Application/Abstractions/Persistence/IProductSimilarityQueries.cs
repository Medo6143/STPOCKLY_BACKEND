namespace Stockly.Application.Abstractions.Persistence;

/// <summary>
/// Fuzzy lookups for matching (docs/design/backend.md §8, Sprint 6): <c>pg_trgm</c> similarity of each line against the
/// current shop's non-archived products and learned aliases of every supplier. Tenant-filtered, read records only.
/// </summary>
public interface IProductSimilarityQueries
{
    /// <summary>
    /// For each probe, the best candidates (at most <see cref="MaxCandidatesPerLine"/>, most similar first) whose
    /// similarity is at least <paramref name="threshold"/>: products by normalized name or <c>latin_key</c>, aliases by
    /// normalized alias or <c>latin_key</c>, the higher of the two similarities. One query for all probes.
    /// </summary>
    Task<IReadOnlyList<SimilarCandidate>> FindSimilarAsync(
        Guid? supplierId, IReadOnlyList<SimilarityProbe> probes, double threshold, CancellationToken cancellationToken);

    /// <summary>Candidates returned per line at most.</summary>
    const int MaxCandidatesPerLine = 5;
}

/// <summary>A line to compare: its number, its Arabic-normalized name and the name's <c>LatinKey</c>.</summary>
public sealed record SimilarityProbe(int LineNo, string NormalizedName, string LatinKey);

/// <summary>
/// A product similar to line <see cref="LineNo"/>. From an alias: <see cref="AliasUnitId"/> is the alias's unit and
/// <see cref="OtherSupplier"/> says whether the alias was learned from another supplier than the invoice's (or with a
/// different known/unknown supplier); <see cref="Units"/> is empty. From the product name: <see cref="AliasUnitId"/> is
/// <c>null</c> and <see cref="Units"/> lists the product's units. <see cref="Similarity"/> is in 0..1.
/// </summary>
public sealed record SimilarCandidate(
    int LineNo, Guid ProductId, Guid? AliasUnitId, bool OtherSupplier, double Similarity, IReadOnlyList<NamedProductUnit> Units);
