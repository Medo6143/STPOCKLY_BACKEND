using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Api.RateLimiting;
using Stockly.Application.Features.Admin.AdminLogin;
using Stockly.Application.Features.Admin.AdminRefresh;
using Stockly.Application.Features.Auth.Logout;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Auth;

namespace Stockly.Api.Controllers.Admin;

/// <summary>Contract AD1–AD3 (§11). Maps HTTP to MediatR and back; no business decisions here (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/admin/auth")]
public sealed class AdminAuthController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>AD1: sign a platform admin in. Rate-limited per client IP; locked accounts get 423.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimiting.AdminLoginPolicy)]
    [ProducesResponseType<AdminAuthResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, StocklyProblems.ContentType)]
    public async Task<IActionResult> LoginAsync(AdminLoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new AdminLoginCommand(request.Email, request.Password), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD2: rotate an admin refresh token. A shop refresh token is <c>401 Auth.InvalidRefreshToken</c>.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AdminAuthResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    public async Task<IActionResult> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new AdminRefreshCommand(request.RefreshToken), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>AD3: end this admin session (revokes the token's family). Idempotent.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = AuthPolicies.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }
}
