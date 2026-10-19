using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.ApplyPaymentCallback;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract §12.3: <c>POST /api/payments/paymob/callback</c>, Paymob's transaction processed callback. Anonymous and
/// verified by its HMAC (the gateway). An operations endpoint like <c>/health</c>: not in the endpoint index, so it is
/// left out of the OpenAPI document. Invalid signature → <c>401</c> with no body; every verified callback → <c>200</c>
/// (repeats, unknown orders and mismatches change nothing). Logs the payment id and the outcome only, never the body
/// or the signature (AGENTS.md rule 12).
/// </summary>
[ApiController]
[Route("api/payments/paymob")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed partial class PaymentCallbacksController(ISender sender, ILogger<PaymentCallbacksController> logger) : ControllerBase
{
    [HttpPost("callback")]
    public async Task<IActionResult> CallbackAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var query = Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await sender.Send(new ApplyPaymentCallbackCommand(new ProviderCallback(body, query)), cancellationToken);
        if (result.IsFailure)
        {
            // No body at all (contract §12.3): neither the client-error mapping nor the status code pages add one.
            if (HttpContext.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            {
                statusCodePages.Enabled = false;
            }

            Response.StatusCode = StocklyProblems.StatusFor(result.Error.Type);
            return new EmptyResult();
        }

        LogCallback(logger, result.Value.PaymentId, result.Value.Outcome);
        return Ok();
    }

    [LoggerMessage(EventId = 120, Level = LogLevel.Information, Message = "Paymob callback for payment {PaymentId}: {Outcome}")]
    private static partial void LogCallback(ILogger logger, Guid? paymentId, SettlementOutcome outcome);
}
