using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.AdminRefresh;

/// <summary>
/// A3's rotation for admins: locks the presented token's row; revoked → revoke its family and fail with
/// <c>Auth.RefreshTokenReused</c>; unknown, expired, or a token of a user who is not a platform admin (a shop refresh
/// token) → <c>Auth.InvalidRefreshToken</c>; otherwise rotate within the family.
/// </summary>
public sealed class AdminRefreshCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUserAccounts users,
    IPlatformAdminAccounts admins,
    ITokenService tokens,
    AdminSessions sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<AdminRefreshCommand, Result<AdminAuthResponse>>
{
    public async Task<Result<AdminAuthResponse>> Handle(AdminRefreshCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var current = await refreshTokens.FindByHashForUpdateAsync(tokens.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (current is null || !await admins.IsPlatformAdminAsync(current.UserId, cancellationToken))
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

        var admin = await users.FindByIdAsync(current.UserId, cancellationToken);
        if (admin is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var response = sessions.Rotate(current, admin);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
