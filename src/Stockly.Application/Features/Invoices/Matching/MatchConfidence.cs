namespace Stockly.Application.Features.Invoices.Matching;

/// <summary>Confidence values of matching v1 (docs/design/backend.md §8).</summary>
public static class MatchConfidence
{
    /// <summary>The supplier's learned alias for this text.</summary>
    public const decimal Alias = 1.00m;

    /// <summary>Exact product name and a certain unit (same unit text, or no unit text and a single unit).</summary>
    public const decimal NameAndUnit = 0.90m;

    /// <summary>Exact product name, unit text not found (or missing with several units): the base unit.</summary>
    public const decimal NameWithBaseUnit = 0.70m;
}
