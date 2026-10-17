using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.GetAdminInvoice;
using Stockly.Application.Features.Admin.GetFailedInvoices;
using Stockly.Application.Features.Admin.RerunInvoice;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;

namespace Stockly.Api.Controllers.Admin;

/// <summary>Contract AD17–AD19 (§11): failed scans of every shop. Maps HTTP to MediatR and back (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/admin/invoices")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminInvoicesController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD17: a page of failed invoices, newest first, of every shop or of <c>shopId</c>.</summary>
    [HttpGet("failed")]
    [ProducesResponseType<PagedResponse<AdminFailedInvoice>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListFailedAsync(
        [FromQuery] Guid? shopId = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetFailedInvoicesQuery(shopId, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD18: an invoice of any shop with a 5-minute image URL, the raw AI output and its AI usage.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminInvoiceDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminInvoiceQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD19: process a failed invoice again (AI cache bypassed, no quota charged); <c>202</c> while it runs.</summary>
    [HttpPost("{id:guid}/rerun")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status202Accepted, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> RerunAsync(Guid id, AdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new RerunInvoiceCommand(id, request.Reason), cancellationToken);
        return result.ToActionResult(this, invoice => Accepted($"/api/admin/invoices/{invoice.Id:D}", invoice));
    }
}
