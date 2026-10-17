using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>AGENTS.md rule 16: the printed total and the sum of the lines differ by more than 2% (<see cref="TotalsTolerancePolicy"/>).</summary>
public sealed class TotalMismatchRule : IInvoiceValidationRule
{
    public int Order => 30;

    public InvoiceValidationStage Stages => InvoiceValidationStage.Extraction | InvoiceValidationStage.Review;

    public ValueTask<InvoiceWarning?> EvaluateAsync(InvoiceValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult<InvoiceWarning?>(
            TotalsTolerancePolicy.IsMismatch(context.TotalAmount, context.ComputedTotal) ? InvoiceWarning.TotalMismatch : null);
    }
}
