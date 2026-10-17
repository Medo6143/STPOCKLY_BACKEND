using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Invoices;

namespace Stockly.Application.Features.Invoices.UpdateInvoiceItems;

/// <summary>
/// I4: replaces the header fields and all lines of an invoice waiting for review and recomputes the warnings. Never
/// changes stock (AGENTS.md rule 19).
/// </summary>
public sealed record UpdateInvoiceItemsCommand(
    Guid InvoiceId,
    IReadOnlyList<InvoiceLineRequest>? Items,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? TotalAmount) : ICommand<InvoiceDetail>;
