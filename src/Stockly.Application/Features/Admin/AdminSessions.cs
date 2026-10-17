using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Domain.Users;

namespace Stockly.Application.Features.Admin;

/// <summary>
/// Issues the token pair of an admin session (contract §11.1): the same lifetimes, refresh rotation and token families as
/// a shop session, with an admin access token (no shop). The caller saves the unit of work.
/// </summary>
public sealed class AdminSessions(ITokenService tokens, IRefreshTokenRepository refreshTokens, IClock clock)
{
    private const long MicrosecondTicks = TimeSpan.TicksPerMicrosecond;

    /// <summary>Starts a new refresh token family (AD1).</summary>
    public AdminAuthResponse Start(UserAccount admin)
    {
        ArgumentNullException.ThrowIfNull(admin);
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.StartFamily(admin.Id, refresh.Hash, deviceId: null, Now(), TokenLifetimes.RefreshToken);
        refreshTokens.Add(stored);
        return Build(admin, refresh.Token, stored.ExpiresAt);
    }

    /// <summary>Rotates the presented token within its family (AD2).</summary>
    public AdminAuthResponse Rotate(RefreshToken current, UserAccount admin)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(admin);
        var refresh = tokens.CreateRefreshToken();
        var successor = current.Rotate(refresh.Hash, Now(), TokenLifetimes.RefreshToken);
        refreshTokens.Add(successor);
        return Build(admin, refresh.Token, successor.ExpiresAt);
    }

    // timestamptz keeps microseconds: truncate so the expiry in the response equals the stored one.
    private DateTimeOffset Now()
    {
        var now = clock.UtcNow;
        return now.AddTicks(-(now.Ticks % MicrosecondTicks));
    }

    private AdminAuthResponse Build(UserAccount admin, string refreshToken, DateTimeOffset refreshExpiresAt)
    {
        var access = tokens.CreateAdminAccessToken(admin.Id);
        return new AdminAuthResponse(
            access.Token,
            access.ExpiresAt.UtcDateTime,
            refreshToken,
            refreshExpiresAt.UtcDateTime,
            new AdminUserDto(admin.Id, admin.Email, admin.FullName));
    }
}
