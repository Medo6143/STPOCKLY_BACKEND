using System.Globalization;
using System.Resources;

namespace Stockly.Application.Validation;

/// <summary>
/// Stockly's own validation messages in the request's UI culture (<c>Resources/ValidationMessages*.resx</c>, ar and
/// en). FluentValidation's built-in messages are localized by FluentValidation itself.
/// </summary>
public static class ValidationMessages
{
    private static readonly ResourceManager Resources =
        new("Stockly.Application.Resources.ValidationMessages", typeof(ValidationMessages).Assembly);

    public static string PasswordNeedsLetter => Get(nameof(PasswordNeedsLetter));

    public static string PasswordNeedsDigit => Get(nameof(PasswordNeedsDigit));

    public static string PasswordRejected => Get(nameof(PasswordRejected));

    public static string LanguageNotSupported => Get(nameof(LanguageNotSupported));

    private static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"Validation message '{key}' is missing.");
}
