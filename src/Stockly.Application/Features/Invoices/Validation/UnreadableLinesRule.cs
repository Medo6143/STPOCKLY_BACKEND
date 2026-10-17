using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>Some extracted lines could not be read and were not saved; the user adds them in review.</summary>
public sealed class UnreadableLinesRule : IInvoiceValidationRule
{
    public int Order => 20;

    public InvoiceValidationStage Stages => InvoiceValidationStage.Extraction;

    public ValueTask<InvoiceWarning?> EvaluateAsync(InvoiceValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult<InvoiceWarning?>(context.UnreadableLineCount > 0 ? InvoiceWarning.UnreadableLines : null);
    }
}
