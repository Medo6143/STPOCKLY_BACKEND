using Stockly.Application.Abstractions.Identity;

namespace Stockly.Api.Identity;

/// <summary>The <c>sub</c> claim of the authenticated user. Missing, malformed, empty or ambiguous claims give <c>null</c>.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var values = accessor.HttpContext?.User.FindAll(StocklyClaimTypes.Subject).Select(claim => claim.Value).Distinct().ToList();
            return values is [var single] && Guid.TryParse(single, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }
}
