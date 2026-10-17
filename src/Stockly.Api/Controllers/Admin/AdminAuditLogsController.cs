using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.GetAuditLogs;
using Stockly.Application.Validation;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Api.Controllers.Admin;

/// <summary>Contract AD20 (§11): the admin audit log. Maps HTTP to MediatR and back (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public sealed class AdminAuditLogsController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD20: a page of entries, newest first, optionally of one target type, target or admin.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<AuditLogEntry>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? targetType = null,
        [FromQuery] Guid? targetId = null,
        [FromQuery] Guid? adminUserId = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAuditLogsQuery(targetType, targetId, adminUserId, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
