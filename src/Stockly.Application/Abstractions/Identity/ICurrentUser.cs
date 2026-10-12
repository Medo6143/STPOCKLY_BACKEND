namespace Stockly.Application.Abstractions.Identity;

/// <summary>The authenticated user of the current request, from the access token's <c>sub</c> claim.</summary>
public interface ICurrentUser
{
    /// <summary><c>null</c> when the request is anonymous or the claim is missing or malformed.</summary>
    Guid? UserId { get; }
}
