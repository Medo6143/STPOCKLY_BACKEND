using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Billing.CancelPayment;
using Stockly.Application.Features.Billing.Checkout;
using Stockly.Application.Features.Billing.GetBillingPlans;
using Stockly.Application.Features.Billing.GetPayment;
using Stockly.Application.Features.Billing.GetPayments;
using Stockly.Application.Features.Billing.GetSubscription;
using Stockly.Application.Validation;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract §12.2: B1–B6 (plans and payments, ADR-0015). Maps HTTP to MediatR and back; no business decisions here
/// (AGENTS.md rule 26). Owner-only rows use the <see cref="AuthPolicies.Owner"/> policy; the shop always comes from the
/// token (ADR-0002).
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public sealed class BillingController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>B1: the active plans, free first, then by price; the shop's current plan marked.</summary>
    [HttpGet("plans")]
    [ProducesResponseType<IReadOnlyList<BillingPlan>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> PlansAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetBillingPlansQuery(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>B2: the shop's subscription, usage today, members and newest pending payment.</summary>
    [HttpGet("subscription")]
    [ProducesResponseType<SubscriptionSummary>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> SubscriptionAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSubscriptionQuery(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>
    /// B3 (owner): start a payment for one month of a plan. A new payment is <c>201</c>; a repeat of the same
    /// <c>clientRequestId</c> is <c>200</c> with the existing payment. The amount is the plan's price.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Policy = AuthPolicies.Owner)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, StocklyProblems.ContentType)]
    public async Task<IActionResult> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CheckoutCommand(request.ClientRequestId, request.PlanId, request.Method, request.WalletPhone), cancellationToken);
        return result.ToCreatedOrOk(this, checkout => checkout.Created, checkout => checkout.Payment, checkout => PaymentLocation(checkout.Payment.Id));
    }

    /// <summary>B4: one payment of the shop.</summary>
    [HttpGet("payments/{id:guid}")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> PaymentAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPaymentQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>B5: a page of the shop's payments, newest first.</summary>
    [HttpGet("payments")]
    [ProducesResponseType<PagedResponse<PaymentDto>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> PaymentsAsync(
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetPaymentsQuery(page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>B6 (owner): cancel a pending payment (it becomes <c>Expired</c>).</summary>
    [HttpPost("payments/{id:guid}/cancel")]
    [Authorize(Policy = AuthPolicies.Owner)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelPaymentCommand(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    private static string PaymentLocation(Guid paymentId) => $"/api/billing/payments/{paymentId:D}";
}
