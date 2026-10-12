using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Auth.Logout;

/// <summary>Only the caller's own tokens are revoked; another user's token is ignored (still a success, nothing leaks).</summary>
public sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenService tokens,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.Unauthenticated;
        }

        var token = await refreshTokens.FindByHashAsync(tokens.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (token is not null && token.UserId == userId)
        {
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, clock.UtcNow, cancellationToken);
        }

        return Result.Success();
    }
}
