using MediatR;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Invoices.ScanInvoice;

/// <summary>
/// I1: upload an invoice photo. <see cref="ClientRequestId"/> is the form's string (a UUID, validated). Not wrapped in the
/// transaction behavior: the handler uploads the image before its single insert (docs/design/backend.md §3.2).
/// </summary>
public sealed record ScanInvoiceCommand(string? ClientRequestId, InvoiceImageUpload? Image) : IRequest<Result<ScanInvoiceResult>>;

/// <summary>The uploaded file as the Api received it; Application never sees <c>IFormFile</c>.</summary>
public sealed record InvoiceImageUpload(Stream Content, string? ContentType, long Length);

/// <summary>The invoice and whether this request created it (202) or repeated an earlier scan (200).</summary>
public sealed record ScanInvoiceResult(InvoiceListItem Invoice, bool Created);
