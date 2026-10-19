using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.GetPayments;
using Stockly.Application.Features.Admin.GetPaymentSummary;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;

namespace Stockly.Api.Controllers.Admin;

/// <summary>Contract AD21–AD22 (§12.4): payments of every shop and paid revenue. Maps HTTP to MediatR and back (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/admin/payments")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminPaymentsController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD21: a page of payments, newest first, of every shop or of <c>shopId</c>, optionally of one <c>status</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<AdminPayment>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] Guid? shopId = null,
        [FromQuery] PaymentStatus? status = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetPaymentsQuery(shopId, status, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>
    /// AD22: paid count and amount, in total and per method, for Africa/Cairo days <c>from</c>–<c>to</c> (both included,
    /// default the last 30 days, at most 92 days apart).
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<PaymentSummary>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> SummaryAsync(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetPaymentSummaryQuery(from, to), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
