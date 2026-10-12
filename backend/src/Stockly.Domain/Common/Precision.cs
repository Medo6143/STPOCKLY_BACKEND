namespace Stockly.Domain.Common;

/// <summary>
/// Precision rules for money, quantities and unit factors (docs/erd.md §1). Values with more decimal
/// places than allowed are rejected, never rounded. Limits are exclusive and match the column types.
/// </summary>
public static class Precision
{
    /// <summary>Money: numeric(18,2).</summary>
    public const int MoneyDecimals = 2;

    /// <summary>Quantities in base units: numeric(14,3).</summary>
    public const int QuantityDecimals = 3;

    /// <summary>Unit factors: numeric(14,6).</summary>
    public const int FactorDecimals = 6;

    private const decimal MoneyLimit = 10_000_000_000_000_000m;
    private const decimal QuantityLimit = 100_000_000_000m;
    private const decimal FactorLimit = 100_000_000m;

    /// <summary>Money: at least 0, at most 2 decimal places.</summary>
    public static Error? ValidateMoney(decimal value, string field)
    {
        if (value < 0m)
        {
            return PrecisionErrors.Negative(field);
        }

        return Validate(value, field, MoneyDecimals, MoneyLimit);
    }

    /// <summary>Quantity or quantity change: any sign, at most 3 decimal places.</summary>
    public static Error? ValidateQuantity(decimal value, string field) =>
        Validate(value, field, QuantityDecimals, QuantityLimit);

    /// <summary>Quantity that cannot be negative (thresholds, counted stock).</summary>
    public static Error? ValidateNonNegativeQuantity(decimal value, string field)
    {
        if (value < 0m)
        {
            return PrecisionErrors.Negative(field);
        }

        return ValidateQuantity(value, field);
    }

    /// <summary>Unit factor: greater than 0, at most 6 decimal places.</summary>
    public static Error? ValidateFactor(decimal value, string field)
    {
        if (value <= 0m)
        {
            return PrecisionErrors.NotPositive(field);
        }

        return Validate(value, field, FactorDecimals, FactorLimit);
    }

    private static Error? Validate(decimal value, string field, int maxDecimals, decimal limit)
    {
        if (decimal.Round(value, maxDecimals) != value)
        {
            return PrecisionErrors.TooManyDecimals(field, maxDecimals);
        }

        return Math.Abs(value) >= limit ? PrecisionErrors.OutOfRange(field) : null;
    }
}
