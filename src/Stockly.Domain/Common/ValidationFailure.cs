namespace Stockly.Domain.Common;

/// <summary>The request-shape failure (contract §1.8): code <c>Validation.Failed</c> with per-field messages.</summary>
public static class ValidationFailure
{
    public const string Code = "Validation.Failed";

    public static Error WithFieldErrors(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        Error.Validation(Code, "One or more fields are invalid.") with { FieldErrors = fieldErrors };
}
