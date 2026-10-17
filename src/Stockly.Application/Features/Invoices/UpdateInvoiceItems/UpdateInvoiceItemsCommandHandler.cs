using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Application.Validation;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Text;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Features.Invoices.UpdateInvoiceItems;

/// <summary>
/// I4 (docs/design/backend.md §9.1, §17.1 item 11), inside the transaction behavior: parse the decimal strings, require a
/// product or a new product on every line, lock the invoice (rule 4: only the invoice is locked), check the products and
/// units in one query, replace the lines, recompute the Review-stage warnings and return the projection. No stock change.
/// </summary>
public sealed class UpdateInvoiceItemsCommandHandler(
    IInvoiceRepository invoices,
    IInvoiceQueries queries,
    IProductRepository products,
    ISupplierRepository suppliers,
    InvoiceValidator invoiceValidator,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateInvoiceItemsCommand, Result<InvoiceDetail>>
{
    public async Task<Result<InvoiceDetail>> Handle(UpdateInvoiceItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);

        var parsed = Parse(request);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var (header, lines) = parsed.Value;
        if (lines.Any(line => line.Product is null && line.NewProduct is null))
        {
            return InvoiceErrors.LineProductRequired;
        }

        var invoice = await invoices.FindForUpdateAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.Status != DomainInvoiceStatus.NeedsReview)
        {
            return InvoiceErrors.NotEditable;
        }

        var productError = await CheckProductsAsync(invoice, lines, cancellationToken);
        if (productError is not null)
        {
            return productError;
        }

        var supplierId = header.SupplierName is { } supplierName
            ? await suppliers.FindIdByNormalizedNameAsync(ArabicTextNormalizer.Normalize(supplierName), cancellationToken)
            : null;
        var warnings = await invoiceValidator.ValidateAsync(
            new InvoiceValidationContext(
                InvoiceValidationStage.Review,
                invoice.Id,
                supplierId,
                header.InvoiceNumber,
                header.TotalAmount,
                lines.Sum(line => InvoiceItem.ComputeLineTotal(line.Quantity, line.UnitPrice)),
                lines.Count,
                UnreadableLineCount: 0,
                Truncated: false),
            cancellationToken);

        // The previous lines are deleted before the new ones are added (nothing references them before confirm).
        invoices.RemoveItems([.. invoice.Items]);
        var replaced = invoice.ReplaceLines(header, lines, supplierId, warnings);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var detail = await queries.FindDetailAsync(invoice.Id, cancellationToken);
        return detail ?? throw new InvalidOperationException("The invoice updated in this transaction was not found.");
    }

    private static Result<(InvoiceHeader Header, List<InvoiceReviewLine> Lines)> Parse(UpdateInvoiceItemsCommand request)
    {
        decimal? total = null;
        if (request.TotalAmount is { } totalText)
        {
            var parsedTotal = DecimalInput.Money(totalText, FieldPath.ToCamelCase(nameof(request.TotalAmount)));
            if (parsedTotal.IsFailure)
            {
                return parsedTotal.Error;
            }

            total = parsedTotal.Value;
        }

        var lines = new List<InvoiceReviewLine>(request.Items!.Count);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var line = request.Items[index];
            var field = FieldPath.ToCamelCase($"{nameof(request.Items)}[{index}]") + ".";
            var quantity = DecimalInput.Quantity(line.Quantity, field + FieldPath.ToCamelCase(nameof(line.Quantity)));
            var unitPrice = DecimalInput.Money(line.UnitPrice, field + FieldPath.ToCamelCase(nameof(line.UnitPrice)));
            var sellPrice = line.NewProduct is { } newProduct
                ? DecimalInput.Money(
                    newProduct.SellPrice,
                    field + FieldPath.ToCamelCase($"{nameof(line.NewProduct)}.{nameof(newProduct.SellPrice)}"))
                : Result.Success(0m);
            if (DecimalInput.FirstError(quantity, unitPrice, sellPrice) is { } error)
            {
                return error;
            }

            lines.Add(new InvoiceReviewLine(
                line.LineNo,
                line.RawName,
                quantity.Value,
                unitPrice.Value,
                line.ExpiryDate,
                line.BatchNo,
                line is { ProductId: { } productId, ProductUnitId: { } unitId } ? new InvoiceLineProduct(productId, unitId) : null,
                line.NewProduct is { } created ? new InvoiceNewProduct(created.Name, created.BaseUnitName, sellPrice.Value) : null));
        }

        var header = new InvoiceHeader(
            OptionalText.Clean(request.SupplierName), OptionalText.Clean(request.InvoiceNumber), request.InvoiceDate, total);
        return (header, lines);
    }

    // Lines in request order: the first failing line decides the error. Another shop's product is invisible (404).
    private async Task<Error?> CheckProductsAsync(Invoice invoice, List<InvoiceReviewLine> lines, CancellationToken cancellationToken)
    {
        var productIds = lines.Where(line => line.Product is not null).Select(line => line.Product!.ProductId).Distinct().ToList();
        if (productIds.Count == 0)
        {
            return null;
        }

        var found = (await products.FindUnitsInfoAsync(productIds, cancellationToken)).ToDictionary(product => product.Id);
        foreach (var line in lines)
        {
            if (line.Product is not { } reference)
            {
                continue;
            }

            if (!found.TryGetValue(reference.ProductId, out var product))
            {
                return ProductErrors.NotFound;
            }

            if (!product.UnitIds.Contains(reference.ProductUnitId))
            {
                return await products.UnitExistsAsync(reference.ProductUnitId, cancellationToken)
                    ? InvoiceErrors.UnitNotOfProduct
                    : ProductUnitErrors.NotFound;
            }

            // An archived product stays only on the line that already had it (backend.md §17.1 item 11).
            if (product.IsArchived && !invoice.HasProductOnLine(line.LineNo, product.Id))
            {
                return ProductErrors.NotFound;
            }
        }

        return null;
    }
}
