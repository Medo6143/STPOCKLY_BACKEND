using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Auth.GetMe;
using Stockly.Application.Features.Auth.Login;
using Stockly.Application.Features.Auth.Logout;
using Stockly.Application.Features.Auth.Refresh;
using Stockly.Application.Features.Auth.Register;
using Stockly.Contracts.Auth;

namespace Stockly.Api.Controllers;

/// <summary>Contract rows A1–A5. Maps HTTP to MediatR and back; no business decisions here (AGENTS.md rule 26).</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    private const string MeLocation = "/api/auth/me";
    private const string Json = "application/json";

    /// <summary>A1: register a user with their shop. Returns the first session.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new RegisterCommand(
            request.FullName, request.Email, request.Password, request.Phone, request.PreferredLanguage, request.DeviceId, request.Shop);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, response => Created(MeLocation, response));
    }

    /// <summary>A2: sign in to the active membership joined first.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new LoginCommand(request.Email, request.Password, request.DeviceId), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>A3: rotate the refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new RefreshCommand(request.RefreshToken), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>A4: end this device session. Allowed for suspended shops.</summary>
    [HttpPost("logout")]
    [Authorize(Policy = AuthPolicies.AllowSuspendedShop)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    public async Task<IActionResult> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }

    /// <summary>A5: restore the session at app start-up.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> MeAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMeQuery(), cancellationToken);
        return result.ToActionResult(this, Ok);
    }
}
