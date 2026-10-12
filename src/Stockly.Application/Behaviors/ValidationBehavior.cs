using FluentValidation;
using MediatR;
using Stockly.Application.Validation;
using Stockly.Domain.Common;

namespace Stockly.Application.Behaviors;

/// <summary>
/// Runs every FluentValidation validator of the request before its handler. Failures become one
/// <c>Validation.Failed</c> error whose field keys are camelCase paths (contract §1.8); the handler is not called.
/// Messages use the request's UI culture.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var fieldErrors = failures
            .GroupBy(failure => FieldPath.ToCamelCase(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return FailureResult.Create<TResponse>(Domain.Common.ValidationFailure.WithFieldErrors(fieldErrors));
    }
}
