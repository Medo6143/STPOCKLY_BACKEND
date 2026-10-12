using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Stockly.Domain.Common;

namespace Stockly.Application.Validation;

/// <summary>
/// The one parser for money, quantity and factor strings in requests (contract §1.2). The shape
/// (<c>^-?\d{1,17}(\.\d+)?$</c>) is a request-shape rule checked by the validators (<see cref="DecimalString{T, TText}"/>,
/// <c>400 Validation.Failed</c>). The handler then parses with the Domain <see cref="Precision"/> rules, which give
/// <c>Precision.Negative</c>, <c>Precision.NotPositive</c>, <c>Precision.TooManyDecimals</c> (never rounded, also
/// beyond what <see cref="decimal"/> can hold) and <c>Precision.OutOfRange</c>.
/// </summary>
public static partial class DecimalInput
{
    private const char Minus = '-';
    private const char Point = '.';

    public static bool IsWellFormed(string? text) => text is not null && Shape().IsMatch(text);

    /// <summary>Money: at least 0, at most 2 decimal places.</summary>
    public static Result<decimal> Money(string text, string field) =>
        Parse(text, field, Precision.MoneyDecimals, Sign.NonNegative, Precision.ValidateMoney);

    /// <summary>Quantity or quantity change: any sign, at most 3 decimal places.</summary>
    public static Result<decimal> Quantity(string text, string field) =>
        Parse(text, field, Precision.QuantityDecimals, Sign.Any, Precision.ValidateQuantity);

    /// <summary>Quantity that cannot be negative (thresholds, counts, opening stock).</summary>
    public static Result<decimal> NonNegativeQuantity(string text, string field) =>
        Parse(text, field, Precision.QuantityDecimals, Sign.NonNegative, Precision.ValidateNonNegativeQuantity);

    /// <summary>Unit factor: greater than 0, at most 6 decimal places.</summary>
    public static Result<decimal> Factor(string text, string field) =>
        Parse(text, field, Precision.FactorDecimals, Sign.Positive, Precision.ValidateFactor);

    /// <summary>The error of the first failed parse, in argument order; <c>null</c> when every value parsed.</summary>
    public static Error? FirstError(params Result[] parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return Array.Find(parsed, result => result.IsFailure)?.Error;
    }

    /// <summary>Validator rule: the value is a well-formed decimal string (a JSON number never reaches here: binding rejects it).</summary>
    /// <remarks>A <c>null</c> value passes; required fields add <c>NotNull()</c>.</remarks>
    public static IRuleBuilderOptions<T, TText> DecimalString<T, TText>(this IRuleBuilder<T, TText> rule)
        where TText : class? =>
        rule.Must(text => text is not string value || IsWellFormed(value)).WithMessage(_ => ValidationMessages.DecimalFormat);

    private static Result<decimal> Parse(
        string text, string field, int maxDecimals, Sign sign, Func<decimal, string, Error?> validate)
    {
        if (!IsWellFormed(text))
        {
            return ValidationFailure.WithFieldErrors(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [ValidationMessages.DecimalFormat] });
        }

        var unsigned = text.TrimStart(Minus);
        var pointAt = unsigned.IndexOf(Point, StringComparison.Ordinal);
        var integerPart = pointAt < 0 ? unsigned : unsigned[..pointAt];
        var fraction = pointAt < 0 ? string.Empty : unsigned[(pointAt + 1)..].TrimEnd('0');
        var isZero = integerPart.All(digit => digit == '0') && fraction.Length == 0;
        var isNegative = text[0] == Minus && !isZero;

        if (sign == Sign.NonNegative && isNegative)
        {
            return PrecisionErrors.Negative(field);
        }

        if (sign == Sign.Positive && (isNegative || isZero))
        {
            return PrecisionErrors.NotPositive(field);
        }

        if (fraction.Length > maxDecimals)
        {
            return PrecisionErrors.TooManyDecimals(field, maxDecimals);
        }

        // At most 17 integer digits and maxDecimals (<= 6) fraction digits: exact in decimal.
        var magnitude = decimal.Parse(
            fraction.Length == 0 ? integerPart : integerPart + Point + fraction,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
        var value = isNegative ? -magnitude : magnitude;
        var error = validate(value, field);
        return error is null ? value : error;
    }

    // [0-9], not \d: .NET's \d also matches Arabic-Indic digits, which decimal.Parse rejects.
    [GeneratedRegex(@"^-?[0-9]{1,17}(\.[0-9]+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Shape();

    private enum Sign
    {
        Any,
        NonNegative,
        Positive,
    }
}
