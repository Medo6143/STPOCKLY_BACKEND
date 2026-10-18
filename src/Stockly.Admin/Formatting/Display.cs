using System.Globalization;

namespace Stockly.Admin.Formatting;

/// <summary>
/// Turns contract values into text. Money and costs arrive as decimal strings (contract §1.2) and are parsed with
/// <see cref="decimal.Parse(string, IFormatProvider)"/> in the invariant culture, never through <c>double</c>, so no digit
/// changes on the way to the screen.
/// </summary>
public static class Display
{
    public const string Missing = "—";

    private const string UsdPrefix = "$";
    private const string EgpSuffix = " EGP";
    private const string UsdFormat = "0.00000000";
    private const string EgpFormat = "#,##0.00";
    private const string TimestampFormat = "yyyy-MM-dd HH:mm";
    private const string DateFormat = "yyyy-MM-dd";
    private const string CountFormat = "#,##0";

    /// <summary>Parses a contract decimal string (money, usd).</summary>
    public static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// <summary>A USD cost with its 8 decimals unchanged, for example <c>$0.00012345</c>.</summary>
    public static string Usd(string value) => UsdPrefix + ParseDecimal(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>A computed USD amount (a sum of costs) with 8 decimals.</summary>
    public static string Usd(decimal value) => UsdPrefix + value.ToString(UsdFormat, CultureInfo.InvariantCulture);

    /// <summary>An EGP amount with its 2 decimals, for example <c>1,250.50 EGP</c>.</summary>
    public static string Egp(string value) => ParseDecimal(value).ToString(EgpFormat, CultureInfo.InvariantCulture) + EgpSuffix;

    public static string Count(int value) => value.ToString(CountFormat, CultureInfo.InvariantCulture);

    /// <summary>A UTC timestamp in the browser's time zone.</summary>
    public static string Timestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static string Timestamp(DateTime? utc) => utc is { } value ? Timestamp(value) : Missing;

    public static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string Date(DateOnly? date) => date is { } value ? Date(value) : Missing;

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;
}
