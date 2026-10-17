using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.GetAiUsageByModel;
using Stockly.Application.Features.Admin.GetAiUsageByShop;
using Stockly.Application.Features.Admin.GetDailyAiUsage;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Api.Controllers.Admin;

/// <summary>
/// Contract AD14–AD16 (§11): AI usage and cost. <c>from</c>/<c>to</c> are Africa/Cairo dates, both included, at most 92
/// days apart, default the last 30 days. Maps HTTP to MediatR and back (AGENTS.md rule 26).
/// </summary>
[ApiController]
[Route("api/admin/ai-usage")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminAiUsageController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD14: one row per day, days without usage included, oldest first.</summary>
    [HttpGet("daily")]
    [ProducesResponseType<IReadOnlyList<AiUsageDay>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> DailyAsync(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetDailyAiUsageQuery(from, to, shopId), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD15: a page of shops, highest cost first.</summary>
    [HttpGet("by-shop")]
    [ProducesResponseType<PagedResponse<AiUsageByShop>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ByShopAsync(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAiUsageByShopQuery(from, to, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD16: every model, highest cost first.</summary>
    [HttpGet("by-model")]
    [ProducesResponseType<IReadOnlyList<AiUsageByModel>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ByModelAsync(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAiUsageByModelQuery(from, to), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
