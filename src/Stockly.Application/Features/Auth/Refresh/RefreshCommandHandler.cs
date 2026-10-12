using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Auth.Refresh;

/// <summary>
/// Locks the presented token's row, then: revoked → revoke the whole family (other families of the user, i.e. other
/// devices, are untouched) and fail with <c>Auth.RefreshTokenReused</c>; unknown or expired →
/// <c>Auth.InvalidRefreshToken</c>; otherwise rotate within the family. The shop is resolved like at login.
/// </summary>
public sealed class RefreshCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUserAccounts users,
    IShopMembershipRepository memberships,
    ITokenService tokens,
    AuthSessions sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<RefreshCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var current = await refreshTokens.FindByHashForUpdateAsync(tokens.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (current is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (current.IsRevoked)
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return AuthErrors.RefreshTokenReused;
        }

        if (current.IsExpired(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await users.FindByIdAsync(current.UserId, cancellationToken);
        var membership = user is null ? null : await memberships.FindFirstActiveAsync(user.Id, cancellationToken);
        if (user is null || membership is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (membership.ShopStatus == ShopStatus.Suspended)
        {
            return ShopErrors.Suspended;
        }

        var response = sessions.Rotate(current, user, membership);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
