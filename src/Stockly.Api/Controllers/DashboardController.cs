using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Dashboard;
using Stockly.Application.Features.Dashboard.GetDashboardSummary;
using Stockly.Application.Features.Dashboard.GetExpiringSoon;
using Stockly.Application.Validation;
using Stockly.Contracts.Common;
using Stockly.Contracts.Dashboard;

namespace Stockly.Api.Controllers;

/// <summary>Contract §8: D1 and D2 (Sprint 4). Maps HTTP to MediatR and back (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>D1: the shop's day at a glance ("today" is the Africa/Cairo day), cached 2 minutes.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> SummaryAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDashboardSummaryQuery(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>D2: confirmed invoice lines expiring within <c>days</c> whose product still has stock, soonest first.</summary>
    [HttpGet("expiring-soon")]
    [ProducesResponseType<PagedResponse<ExpiringItem>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ExpiringSoonAsync(
        [FromQuery] int days = DashboardLimits.DefaultExpiringDays,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetExpiringSoonQuery(days, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
