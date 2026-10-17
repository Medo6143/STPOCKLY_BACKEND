using FluentValidation;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin;

/// <summary>The reason of an admin write: longer than 500 is <c>400 Validation.Failed</c>; blank is <c>400 Admin.ReasonRequired</c>.</summary>
public static class AdminReason
{
    /// <summary>The trimmed reason, or <c>Admin.ReasonRequired</c> when it is missing or blank.</summary>
    public static Result<string> Require(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? AdminErrors.ReasonRequired : reason.Trim();

    /// <summary>Validator rule: at most <see cref="AdminLimits.ReasonMaxLength"/> characters.</summary>
    public static IRuleBuilderOptions<T, string?> Reason<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(AdminLimits.ReasonMaxLength);
}
