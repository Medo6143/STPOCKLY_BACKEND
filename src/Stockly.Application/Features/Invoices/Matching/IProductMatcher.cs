namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// Matching v1 (docs/design/backend.md §8): finds an existing product and unit for each extracted line. Runs the
/// <see cref="IProductMatchStrategy"/> classes by <see cref="IProductMatchStrategy.Order"/> on the lines still unmatched.
/// </summary>
public interface IProductMatcher
{
    /// <summary>One <see cref="LineMatch"/> per input line, in input order; an unmatched line has only nulls.</summary>
    Task<IReadOnlyList<LineMatch>> MatchAsync(Guid? supplierId, IReadOnlyList<MatchInput> lines, CancellationToken cancellationToken);
}

/// <summary>A line to match: its number and its Arabic-normalized name and unit text (<c>ArabicTextNormalizer</c>).</summary>
public sealed record MatchInput(int LineNo, string NormalizedName, string? NormalizedUnit);

/// <summary>
/// The result for one line: product and unit together (both or neither) and the confidence (1.00, 0.90 or 0.70), or all
/// <c>null</c> when nothing matched.
/// </summary>
public sealed record LineMatch(int LineNo, Guid? ProductId, Guid? ProductUnitId, decimal? Confidence)
{
    public bool IsMatched => ProductId is not null && ProductUnitId is not null && Confidence is not null;

    public static LineMatch None(int lineNo) => new(lineNo, null, null, null);
}
