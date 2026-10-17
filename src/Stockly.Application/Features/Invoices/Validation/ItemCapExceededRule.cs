using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>The model hit the 100-line cap (it set <c>truncated</c>), so later lines are missing.</summary>
public sealed class ItemCapExceededRule : IInvoiceValidationRule
{
    public int Order => 10;

    public InvoiceValidationStage Stages => InvoiceValidationStage.Extraction;

    public ValueTask<InvoiceWarning?> EvaluateAsync(InvoiceValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult<InvoiceWarning?>(context.Truncated ? InvoiceWarning.ItemCapExceeded : null);
    }
}
