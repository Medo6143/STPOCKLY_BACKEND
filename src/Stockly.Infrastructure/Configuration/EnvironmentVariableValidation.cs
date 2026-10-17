using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Stockly.Infrastructure.Configuration;

/// <summary>
/// Validates an options class with its DataAnnotations and names each failing setting as the environment variable to
/// set (<c>Section__Member</c>), like the Api's required settings. Messages never contain a configured value: secrets
/// must not reach startup logs (AGENTS.md rule 12).
/// </summary>
internal sealed class EnvironmentVariableValidation<TOptions>(string sectionName) : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(results.Select(result =>
            $"Set the environment variable {sectionName}__{result.MemberNames.FirstOrDefault()} (locally: copy .env.example to .env): {result.ErrorMessage}"));
    }
}
