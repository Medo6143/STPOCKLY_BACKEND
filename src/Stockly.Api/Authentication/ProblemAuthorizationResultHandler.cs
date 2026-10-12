using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Stockly.Api.Errors;
using Stockly.Domain.Shops;

namespace Stockly.Api.Authentication;

/// <summary>
/// A suspended shop gets <c>403 Shop.Suspended</c>. Every other outcome goes to the default handler, whose
/// challenge and forbid reach <see cref="ProblemJwtBearerEvents"/> (<c>Auth.Unauthenticated</c>, <c>Auth.Forbidden</c>).
/// </summary>
public sealed class ProblemAuthorizationResultHandler(ProblemWriter problemWriter) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(authorizeResult);

        var suspended = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Handler is ActiveShopHandler) == true;

        return suspended
            ? problemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ShopErrors.Suspended.Code)
            : _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
