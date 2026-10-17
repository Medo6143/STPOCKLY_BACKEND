using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>
/// Another invoice of the shop (not failed) has the same invoice number and supplier: probably the same paper scanned
/// twice. Served by <c>ix_invoices_shop_id_supplier_id_invoice_number</c>.
/// </summary>
public sealed class DuplicateInvoiceNumberRule(IInvoiceRepository invoices) : IInvoiceValidationRule
{
    public int Order => 40;

    public InvoiceValidationStage Stages => InvoiceValidationStage.Extraction | InvoiceValidationStage.Review;

    public async ValueTask<InvoiceWarning?> EvaluateAsync(InvoiceValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.InvoiceNumber is not { } number)
        {
            return null;
        }

        return await invoices.ExistsOtherWithNumberAsync(context.InvoiceId, context.SupplierId, number, cancellationToken)
            ? InvoiceWarning.DuplicateInvoiceNumber
            : null;
    }
}
