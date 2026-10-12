using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Auth.Login;

public sealed class LoginCommandHandler(
    IUserAccounts users,
    IShopMembershipRepository memberships,
    AuthSessions sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var check = await users.CheckCredentialsAsync(request.Email.Trim(), request.Password, cancellationToken);
        if (check is { Status: CredentialStatus.LockedOut, LockedOutUntil: { } lockedOutUntil })
        {
            return AuthErrors.LockedOut with { RetryAfter = lockedOutUntil - clock.UtcNow };
        }

        if (check is not { Status: CredentialStatus.Valid, User: { } user })
        {
            return AuthErrors.InvalidCredentials;
        }

        var membership = await memberships.FindFirstActiveAsync(user.Id, cancellationToken);
        if (membership is null)
        {
            return AuthErrors.NoActiveShop;
        }

        if (membership.ShopStatus == ShopStatus.Suspended)
        {
            return ShopErrors.Suspended;
        }

        await users.RecordSignInAsync(user.Id, clock.UtcNow, cancellationToken);
        var response = sessions.Start(user, membership, request.DeviceId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
