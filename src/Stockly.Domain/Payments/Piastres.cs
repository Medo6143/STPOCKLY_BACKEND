using Stockly.Domain.Common;

namespace Stockly.Domain.Payments;

/// <summary>
/// EGP ↔ integer piastres (1 EGP = 100 piastres) for providers that take amounts in minor units (ADR-0015 §6). Computed
/// with <see cref="decimal"/> only (AGENTS.md rule 6); a value with more than 2 decimals is refused, never rounded.
/// </summary>
public static class Piastres
{
    private const decimal PerPound = 100m;

    /// <summary>The amount in piastres. Throws for a negative amount or one with more than 2 decimals.</summary>
    public static long FromPounds(decimal amount)
    {
        if (Precision.ValidateMoney(amount, nameof(amount)) is { } error)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), error.Message);
        }

        return decimal.ToInt64(amount * PerPound);
    }

    /// <summary>The EGP amount of <paramref name="piastres"/>, with 2 decimals.</summary>
    public static decimal ToPounds(long piastres) => decimal.Divide(piastres, PerPound);
}
