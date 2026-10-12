using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Identity;

/// <summary>The authenticated user of the current request, from the access token's <c>sub</c> and <c>role</c> claims.</summary>
public interface ICurrentUser
{
    /// <summary><c>null</c> when the request is anonymous or the claim is missing or malformed.</summary>
    Guid? UserId { get; }

    /// <summary>The user's role in the token's shop; <c>null</c> when the claim is missing, ambiguous or not a role name.</summary>
    ShopRole? Role { get; }
}
