using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Invoices.ScanInvoice;
using Stockly.Contracts.Invoices;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract §6 (I1 in step 2a). Maps HTTP to MediatR and back; no business decisions here (AGENTS.md rule 26). The shop
/// always comes from the token (ADR-0002).
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize]
public sealed class InvoicesController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";
    private const string Multipart = "multipart/form-data";

    /// <summary>
    /// I1: upload an invoice photo. A new scan is <c>202</c> (processing runs in a background job); a repeat of the same
    /// <c>clientRequestId</c> is <c>200</c> with the existing invoice.
    /// </summary>
    [HttpPost("scan")]
    [Consumes(Multipart)]
    [RequestSizeLimit(ScanLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScanLimits.MaxRequestBytes)]
    [ScanUploadLimit]
    [ProducesResponseType<InvoiceListItem>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<InvoiceListItem>(StatusCodes.Status202Accepted, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, StocklyProblems.ContentType)]
    public async Task<IActionResult> ScanAsync([FromForm] ScanInvoiceForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        await using var content = form.Image?.OpenReadStream();
        var image = form.Image is { } file ? new InvoiceImageUpload(content!, file.ContentType, file.Length) : null;
        var result = await sender.Send(new ScanInvoiceCommand(form.ClientRequestId, image), cancellationToken);
        return result.ToAcceptedOrOk(this, scan => scan.Created, scan => scan.Invoice, scan => InvoiceLocation(scan.Invoice.Id));
    }

    private static string InvoiceLocation(Guid invoiceId) => $"/api/invoices/{invoiceId:D}";
}
