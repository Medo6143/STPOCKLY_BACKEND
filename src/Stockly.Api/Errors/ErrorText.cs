using System.Globalization;
using System.Resources;

namespace Stockly.Api.Errors;

/// <summary>
/// Localized ProblemDetails <c>title</c> and <c>detail</c> per error code, in the request's UI culture
/// (<c>Resources/ErrorMessages*.resx</c>: ar and en). Keys are <c>{code}.Title</c> and <c>{code}.Detail</c>.
/// Codes never change with the language (contract §1.5).
/// </summary>
public static class ErrorText
{
    /// <summary>Message for a field whose JSON could not be read (model binding); hides parser and type details.</summary>
    public const string InvalidValueKey = "Validation.InvalidValue";

    private const string TitleSuffix = ".Title";
    private const string DetailSuffix = ".Detail";

    private static readonly ResourceManager Resources =
        new("Stockly.Api.Resources.ErrorMessages", typeof(ErrorText).Assembly);

    public static bool TryGet(string code, out string title, out string detail)
    {
        ArgumentNullException.ThrowIfNull(code);

        title = Resources.GetString(code + TitleSuffix, CultureInfo.CurrentUICulture) ?? string.Empty;
        detail = Resources.GetString(code + DetailSuffix, CultureInfo.CurrentUICulture) ?? string.Empty;
        return title.Length > 0 && detail.Length > 0;
    }

    public static string InvalidValue =>
        Resources.GetString(InvalidValueKey, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"Resource '{InvalidValueKey}' is missing.");
}
