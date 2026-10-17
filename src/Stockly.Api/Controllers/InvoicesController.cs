using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Invoices.ConfirmInvoice;
using Stockly.Application.Features.Invoices.GetInvoice;
using Stockly.Application.Features.Invoices.GetInvoices;
using Stockly.Application.Features.Invoices.ScanInvoice;
using Stockly.Application.Features.Invoices.UpdateInvoiceItems;
using Stockly.Application.Validation;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract §6: I1 (step 2a), I2, I3 and I4 (step 2b), I5 (Sprint 4). Maps HTTP to MediatR and back; no business decisions here
/// (AGENTS.md rule 26). The shop always comes from the token (ADR-0002).
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

    /// <summary>I2: a page of the shop's invoices, newest first, optionally of one status.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<InvoiceListItem>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetInvoicesQuery(status, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>I3: the invoice with its lines. A processing invoice is served from the status cache while the app polls.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInvoiceQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>I4: replace the header and every line of an invoice waiting for review; warnings are recomputed.</summary>
    [HttpPut("{id:guid}/items")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> UpdateItemsAsync(Guid id, UpdateInvoiceItemsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new UpdateInvoiceItemsCommand(
            id, request.Items, request.SupplierName, request.InvoiceNumber, request.InvoiceDate, request.TotalAmount);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>
    /// I5: receive the stock of every line in one transaction, create the new products and learn the aliases. A repeat
    /// returns the confirmed invoice unchanged.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ConfirmInvoiceCommand(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    private static string InvoiceLocation(Guid invoiceId) => $"/api/invoices/{invoiceId:D}";
}
