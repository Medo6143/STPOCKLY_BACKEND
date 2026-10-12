using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Auth;
using Stockly.Domain.Users;

namespace Stockly.Application.Features.Auth;

/// <summary>
/// Issues the token pair of a session. <see cref="Start"/> begins a new refresh token family (one device session);
/// <see cref="Rotate"/> continues the presented token's family. The caller saves the unit of work.
/// </summary>
public sealed class AuthSessions(ITokenService tokens, IRefreshTokenRepository refreshTokens, IClock clock)
{
    private const long MicrosecondTicks = TimeSpan.TicksPerMicrosecond;

    public AuthResponse Start(UserAccount user, ShopMembership membership, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.StartFamily(user.Id, refresh.Hash, deviceId, Now(), TokenLifetimes.RefreshToken);
        refreshTokens.Add(stored);
        return Build(user, membership, refresh.Token, stored.ExpiresAt);
    }

    public AuthResponse Rotate(RefreshToken current, UserAccount user, ShopMembership membership)
    {
        ArgumentNullException.ThrowIfNull(current);
        var refresh = tokens.CreateRefreshToken();
        var successor = current.Rotate(refresh.Hash, Now(), TokenLifetimes.RefreshToken);
        refreshTokens.Add(successor);
        return Build(user, membership, refresh.Token, successor.ExpiresAt);
    }

    // timestamptz keeps microseconds: truncate so the expiry in the response equals the stored one.
    private DateTimeOffset Now()
    {
        var now = clock.UtcNow;
        return now.AddTicks(-(now.Ticks % MicrosecondTicks));
    }

    private AuthResponse Build(UserAccount user, ShopMembership membership, string refreshToken, DateTimeOffset refreshExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(membership);
        var access = tokens.CreateAccessToken(user.Id, membership.ShopId, membership.Role);
        return new AuthResponse(
            access.Token, access.ExpiresAt.UtcDateTime, refreshToken, refreshExpiresAt.UtcDateTime, user.ToDto(), membership.ToDto());
    }
}
