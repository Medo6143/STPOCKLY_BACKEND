namespace Stockly.Application.Abstractions.Identity;

/// <summary>Token lifetimes from the API contract (§1.4).</summary>
public static class TokenLifetimes
{
    public static readonly TimeSpan AccessToken = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(30);
}
