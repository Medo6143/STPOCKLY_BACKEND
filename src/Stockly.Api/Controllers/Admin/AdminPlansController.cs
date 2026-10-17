using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.CreatePlan;
using Stockly.Application.Features.Admin.GetPlans;
using Stockly.Application.Features.Admin.UpdatePlan;
using Stockly.Contracts.Admin;

namespace Stockly.Api.Controllers.Admin;

/// <summary>Contract AD11–AD13 (§11). Maps HTTP to MediatR and back (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminPlansController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD11: every plan, active first, then by code.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlanDto>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPlansQuery(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD12: create a plan; the code is immutable afterwards.</summary>
    [HttpPost]
    [ProducesResponseType<PlanDto>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new CreatePlanCommand(
            request.Code, request.Name, request.DailyScanQuota, request.MaxMembers, request.MonthlyPrice, request.IsActive);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, plan => StatusCode(StatusCodes.Status201Created, plan));
    }

    /// <summary>AD13: change a plan's name, quota, members, price and state.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> UpdateAsync(Guid id, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new UpdatePlanCommand(
            id, request.Name, request.DailyScanQuota, request.MaxMembers, request.MonthlyPrice, request.IsActive);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
