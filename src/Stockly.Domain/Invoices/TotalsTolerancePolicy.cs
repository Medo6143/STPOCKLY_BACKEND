namespace Stockly.Domain.Invoices;

/// <summary>
/// AGENTS.md rule 16: the printed total and the sum of the lines disagree when <c>|computed − total| &gt; 2% of total</c>.
/// No printed total is never a mismatch; a printed total of 0 is a mismatch unless the lines also sum to 0.
/// </summary>
public static class TotalsTolerancePolicy
{
    /// <summary>The allowed difference as a fraction of the printed total.</summary>
    public const decimal Tolerance = 0.02m;

    public static bool IsMismatch(decimal? total, decimal computed)
    {
        if (total is not { } printed)
        {
            return false;
        }

        if (printed == 0m)
        {
            return computed != 0m;
        }

        return Math.Abs(computed - printed) > Tolerance * printed;
    }
}
