using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Shops;

namespace Stockly.Api.Identity;

/// <summary>
/// The <c>sub</c> and <c>role</c> claims of the authenticated user. Missing, malformed, empty or ambiguous claims give
/// <c>null</c>. A role must be the exact PascalCase name (<c>Owner</c>, <c>Staff</c>).
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Single(StocklyClaimTypes.Subject) is { } value && Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : null;

    public ShopRole? Role =>
        Single(StocklyClaimTypes.Role) is { } value
        && Enum.GetNames<ShopRole>().Contains(value, StringComparer.Ordinal)
        && Enum.TryParse<ShopRole>(value, ignoreCase: false, out var role)
            ? role
            : null;

    private string? Single(string claimType)
    {
        var values = accessor.HttpContext?.User.FindAll(claimType).Select(claim => claim.Value).Distinct().ToList();
        return values is [var single] ? single : null;
    }
}
