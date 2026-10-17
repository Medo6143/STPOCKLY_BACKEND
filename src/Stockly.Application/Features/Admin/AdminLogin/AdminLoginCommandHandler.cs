using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.AdminLogin;

/// <summary>
/// Same credential check and lockout as A2. A valid user who is not a platform admin gets <c>Auth.InvalidCredentials</c>,
/// so the endpoint never reveals who is an admin (contract AD1).
/// </summary>
public sealed class AdminLoginCommandHandler(
    IUserAccounts users,
    IPlatformAdminAccounts admins,
    AdminSessions sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<AdminLoginCommand, Result<AdminAuthResponse>>
{
    public async Task<Result<AdminAuthResponse>> Handle(AdminLoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var check = await users.CheckCredentialsAsync(request.Email.Trim(), request.Password, cancellationToken);
        if (check is { Status: CredentialStatus.LockedOut, LockedOutUntil: { } lockedOutUntil })
        {
            return AuthErrors.LockedOut with { RetryAfter = lockedOutUntil - clock.UtcNow };
        }

        if (check is not { Status: CredentialStatus.Valid, User: { } user }
            || !await admins.IsPlatformAdminAsync(user.Id, cancellationToken))
        {
            return AuthErrors.InvalidCredentials;
        }

        await users.RecordSignInAsync(user.Id, clock.UtcNow, cancellationToken);
        var response = sessions.Start(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
