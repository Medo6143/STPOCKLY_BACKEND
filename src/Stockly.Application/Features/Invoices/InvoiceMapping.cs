using System.Globalization;
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

    /// <summary>
    /// I3 for a processing invoice built from its cached snapshot alone (zero PostgreSQL queries, backend.md §2.2): no
    /// header, lines or warnings exist yet.
    /// </summary>
    public static InvoiceDetail ToProcessingDetail(InvoiceStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new InvoiceDetail(
            snapshot.InvoiceId,
            snapshot.ClientRequestId,
            ToContract(snapshot.Status),
            SupplierId: null,
            SupplierName: null,
            InvoiceNumber: null,
            InvoiceDate: null,
            TotalAmount: null,
            ComputedTotal: null,
            Warnings: [],
            FailureReason: null,
            Items: [],
            snapshot.CreatedAt.UtcDateTime,
            ProcessedAt: null,
            ConfirmedAt: null);
    }

    /// <summary>The read-through snapshot of an invoice read from PostgreSQL (backend.md §17.1 item 5).</summary>
    public static InvoiceStatusSnapshot ToStatusSnapshot(InvoiceDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var createdAt = new DateTimeOffset(DateTime.SpecifyKind(detail.CreatedAt, DateTimeKind.Utc));
        var updatedAt = detail.ConfirmedAt ?? detail.ProcessedAt;
        return new InvoiceStatusSnapshot(
            detail.Id,
            (DomainInvoiceStatus)detail.Status,
            updatedAt is { } changed ? new DateTimeOffset(DateTime.SpecifyKind(changed, DateTimeKind.Utc)) : createdAt,
            detail.ClientRequestId,
            createdAt);
    }

    /// <summary>A match confidence as the contract's 2-decimal string (<c>"0.90"</c>).</summary>
    public static string Confidence(decimal value) =>
        value.ToString("F" + InvoiceItem.MatchConfidenceDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static ContractInvoiceStatus ToContract(DomainInvoiceStatus status) => (ContractInvoiceStatus)status;

    public static ContractInvoiceWarning ToContract(DomainInvoiceWarning warning) => (ContractInvoiceWarning)warning;
}
