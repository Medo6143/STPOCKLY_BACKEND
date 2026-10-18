using System.ComponentModel.DataAnnotations;

namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>Configuration section <c>Matching</c> (docs/features/invoice-scan.md, fuzzy matching). Validated at startup.</summary>
public sealed class MatchingOptions
{
    public const string SectionName = "Matching";

    /// <summary>
    /// Lowest <c>pg_trgm</c> similarity that counts as a match (<c>Matching__TrigramThreshold</c>). Not below 0.3: the
    /// query pre-filters with the index-backed <c>%</c> operator, whose PostgreSQL default threshold is 0.3.
    /// </summary>
    [Range(0.3, 1.0)]
    public double TrigramThreshold { get; set; } = 0.45;
}
