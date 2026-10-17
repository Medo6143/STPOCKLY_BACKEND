namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>
/// One way of matching lines to products (SOLID O: a new way is a new class registered in DI). A strategy makes
/// <b>one</b> query for all the lines it receives, never one per line (rule 33), and never matches an archived product.
/// </summary>
public interface IProductMatchStrategy
{
    /// <summary>Lower runs first: exact alias 10, exact name 20 (trigram, Sprint 6: 30).</summary>
    int Order { get; }

    /// <summary>The matches this strategy found, keyed by line number; lines it could not match are absent.</summary>
    Task<IReadOnlyDictionary<int, LineMatch>> MatchAsync(
        Guid? supplierId, IReadOnlyList<MatchInput> unmatched, CancellationToken cancellationToken);
}
