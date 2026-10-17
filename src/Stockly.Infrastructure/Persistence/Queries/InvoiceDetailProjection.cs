using Microsoft.EntityFrameworkCore;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>
/// The I3 <see cref="InvoiceDetail"/> in one statement: the invoice by primary key, its lines by
/// <c>ux_invoice_items_invoice_id_line_no</c> in line order, product and unit names by primary key. The caller passes the
/// sources: tenant-filtered for I3, unfiltered for the platform admin's AD18 (<c>Persistence/Admin</c>). A confirmed
/// new-product line names the product created from it (<c>created_product_id</c>).
/// </summary>
internal static class InvoiceDetailProjection
{
    public static async Task<InvoiceDetail?> FindAsync(
        IQueryable<Invoice> invoices,
        IQueryable<Product> products,
        IQueryable<ProductUnit> units,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var row = await invoices
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.ClientRequestId,
                invoice.Status,
                invoice.SupplierId,
                invoice.SupplierNameRaw,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.TotalAmount,
                invoice.ComputedTotal,
                invoice.Warnings,
                invoice.FailureReason,
                invoice.CreatedAt,
                invoice.ProcessedAt,
                invoice.ConfirmedAt,
                Items = invoice.Items
                    .OrderBy(item => item.LineNo)
                    .Select(item => new ItemRow(
                        item.Id,
                        item.LineNo,
                        item.RawName,
                        item.ProductId ?? item.CreatedProductId,
                        products
                            .Where(product => product.Id == (item.ProductId ?? item.CreatedProductId))
                            .Select(product => product.Name)
                            .FirstOrDefault(),
                        item.ProductUnitId,
                        units
                            .Where(unit => unit.Id == item.ProductUnitId)
                            .Select(unit => unit.Name)
                            .FirstOrDefault(),
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        item.ExpiryDate,
                        item.BatchNo,
                        item.MatchConfidence,
                        item.NewProductName,
                        item.NewProductBaseUnitName,
                        item.NewProductSellPrice))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new InvoiceDetail(
                row.Id,
                row.ClientRequestId,
                InvoiceMapping.ToContract(row.Status),
                row.SupplierId,
                row.SupplierNameRaw,
                row.InvoiceNumber,
                row.InvoiceDate,
                Money(row.TotalAmount),
                Money(row.ComputedTotal),
                [.. row.Warnings.Select(InvoiceMapping.ToContract)],
                row.FailureReason?.ToString(),
                [.. row.Items.Select(ToDto)],
                row.CreatedAt.UtcDateTime,
                row.ProcessedAt?.UtcDateTime,
                row.ConfirmedAt?.UtcDateTime);
    }

    private static InvoiceItemDto ToDto(ItemRow row) => new(
        row.Id,
        row.LineNo,
        row.RawName,
        row.ProductId,
        row.ProductName,
        row.ProductUnitId,
        row.UnitName,
        ProductMapping.Quantity(row.Quantity),
        ProductMapping.Money(row.UnitPrice),
        ProductMapping.Money(row.LineTotal),
        row.ExpiryDate,
        row.BatchNo,
        row.MatchConfidence is { } confidence ? InvoiceMapping.Confidence(confidence) : null,
        row is { NewProductName: { } name, NewProductBaseUnitName: { } baseUnit, NewProductSellPrice: { } sellPrice }
            ? new InvoiceNewProductDto(name, baseUnit, ProductMapping.Money(sellPrice))
            : null);

    private static string? Money(decimal? value) => value is { } amount ? ProductMapping.Money(amount) : null;

    private sealed record ItemRow(
        Guid Id,
        int LineNo,
        string RawName,
        Guid? ProductId,
        string? ProductName,
        Guid? ProductUnitId,
        string? UnitName,
        decimal Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        DateOnly? ExpiryDate,
        string? BatchNo,
        decimal? MatchConfidence,
        string? NewProductName,
        string? NewProductBaseUnitName,
        decimal? NewProductSellPrice);
}
