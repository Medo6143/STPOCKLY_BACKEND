using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;
using ContractInvoiceWarning = Stockly.Contracts.Invoices.InvoiceWarning;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;
using DomainInvoiceWarning = Stockly.Domain.Invoices.InvoiceWarning;

namespace Stockly.Application.Features.Invoices;

/// <summary>Invoice → contract DTOs and the polling snapshot. Contract enums share the Domain enums' names and values.</summary>
public static class InvoiceMapping
{
    public static InvoiceListItem ToListItem(this Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new InvoiceListItem(
            invoice.Id,
            ToContract(invoice.Status),
            invoice.SupplierNameRaw,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.TotalAmount is { } total ? ProductMapping.Money(total) : null,
            invoice.Items.Count,
            [.. invoice.Warnings.Select(ToContract)],
            invoice.CreatedAt.UtcDateTime);
    }

    /// <summary>The status snapshot; it changed when the invoice was processed, or else when it was created.</summary>
    public static InvoiceStatusSnapshot ToStatusSnapshot(this Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new InvoiceStatusSnapshot(
            invoice.Id, invoice.Status, invoice.ConfirmedAt ?? invoice.ProcessedAt ?? invoice.CreatedAt, invoice.ClientRequestId, invoice.CreatedAt);
    }

    public static ContractInvoiceStatus ToContract(DomainInvoiceStatus status) => (ContractInvoiceStatus)status;

    public static ContractInvoiceWarning ToContract(DomainInvoiceWarning warning) => (ContractInvoiceWarning)warning;
}
