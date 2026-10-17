using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Admin.CreateHangfireSession;

namespace Stockly.Api.Controllers.Admin;

/// <summary>
/// <c>POST /api/admin/hangfire/session</c> (contract §11.3): a one-time URL for the Hangfire dashboard. An operations
/// endpoint like <c>/health</c>: not in the endpoint index, so it is left out of the OpenAPI document.
/// </summary>
[ApiController]
[Route("api/admin/hangfire")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AdminHangfireController(ISender sender) : ControllerBase
{
    [HttpPost("session")]
    public async Task<IActionResult> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateHangfireSessionCommand(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
