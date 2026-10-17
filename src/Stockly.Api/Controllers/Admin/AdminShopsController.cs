using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.ActivateShop;
using Stockly.Application.Features.Admin.ChangeSubscription;
using Stockly.Application.Features.Admin.GetShop;
using Stockly.Application.Features.Admin.GetShops;
using Stockly.Application.Features.Admin.ResetScanQuota;
using Stockly.Application.Features.Admin.SetQuotaOverride;
using Stockly.Application.Features.Admin.SuspendShop;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Shops;

namespace Stockly.Api.Controllers.Admin;

/// <summary>
/// Contract AD4–AD10 (§11): every shop, for the platform admin only. Maps HTTP to MediatR and back (AGENTS.md rule 26);
/// the shop id comes from the route because the admin acts on any shop (ADR-0005).
/// </summary>
[ApiController]
[Route("api/admin/shops")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminShopsController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD4: a page of shops, newest first; <c>search</c> matches the name or the owner's email prefix.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<AdminShopListItem>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? search = null,
        [FromQuery] ShopStatus? status = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetShopsQuery(search, status, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD5: one shop with members, subscription, counts, today's scans and the AI cost of 30 days.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminShopDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetShopQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD6: suspend the shop and revoke its members' sessions. Idempotent.</summary>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> SuspendAsync(Guid id, AdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new SuspendShopCommand(id, request.Reason), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }

    /// <summary>AD7: reactivate the shop. Idempotent.</summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> ActivateAsync(Guid id, AdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new ActivateShopCommand(id, request.Reason), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }

    /// <summary>AD8: move the shop to another plan from today; the quota override is carried over.</summary>
    [HttpPut("{id:guid}/subscription")]
    [ProducesResponseType<AdminSubscriptionDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> ChangeSubscriptionAsync(Guid id, ChangeSubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new ChangeSubscriptionCommand(id, request.PlanId, request.EndsOn, request.Reason), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD9: set or remove the daily scan quota override; applies from the next scan.</summary>
    [HttpPut("{id:guid}/quota-override")]
    [ProducesResponseType<AdminSubscriptionDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> SetQuotaOverrideAsync(Guid id, QuotaOverrideRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new SetQuotaOverrideCommand(id, request.DailyScanQuota, request.Reason), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD10: reset today's scan counter of the shop.</summary>
    [HttpPost("{id:guid}/quota/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> ResetQuotaAsync(Guid id, AdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new ResetScanQuotaCommand(id, request.Reason), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }
}
