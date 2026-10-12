using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Net.Http.Headers;
using Stockly.Api.Errors;
using Stockly.Application.Features.Auth;

namespace Stockly.Api.Authentication;

/// <summary>
/// Missing, invalid or expired access token → <c>401 Auth.Unauthenticated</c>; authenticated but not allowed →
/// <c>403 Auth.Forbidden</c>. Both as contract ProblemDetails. Never echoes the token or the validation error.
/// </summary>
public sealed class ProblemJwtBearerEvents(ProblemWriter problemWriter) : JwtBearerEvents
{
    private const string BearerChallenge = "Bearer";

    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.HandleResponse();
        context.Response.Headers[HeaderNames.WWWAuthenticate] = BearerChallenge;
        await problemWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, AuthErrors.Unauthenticated.Code);
    }

    public override Task Forbidden(ForbiddenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return problemWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, AuthErrors.Forbidden.Code);
    }
}
