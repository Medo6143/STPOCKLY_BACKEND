using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>Runs the registered <see cref="IInvoiceValidationRule"/>s of the context's stage by order and collects distinct warnings.</summary>
public sealed class InvoiceValidator(IEnumerable<IInvoiceValidationRule> rules)
{
    private readonly IReadOnlyList<IInvoiceValidationRule> _rules = [.. rules.OrderBy(rule => rule.Order)];

    public async Task<IReadOnlyList<InvoiceWarning>> ValidateAsync(InvoiceValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<InvoiceWarning>();
        foreach (var rule in _rules.Where(rule => rule.Stages.HasFlag(context.Stage)))
        {
            if (await rule.EvaluateAsync(context, cancellationToken) is { } warning && !warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        return warnings;
    }
}
