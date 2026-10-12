namespace Stockly.Domain.Common;

public static class PrecisionErrors
{
    public static Error TooManyDecimals(string field, int maxDecimals) =>
        Error.Validation("Precision.TooManyDecimals", $"'{field}' allows at most {maxDecimals} decimal places.");

    public static Error Negative(string field) =>
        Error.Validation("Precision.Negative", $"'{field}' must not be negative.");

    public static Error NotPositive(string field) =>
        Error.Validation("Precision.NotPositive", $"'{field}' must be greater than zero.");

    public static Error OutOfRange(string field) =>
        Error.Validation("Precision.OutOfRange", $"'{field}' is too large to store.");
}
