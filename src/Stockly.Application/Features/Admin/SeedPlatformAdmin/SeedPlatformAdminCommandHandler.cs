using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Users;

namespace Stockly.Application.Features.Admin.SeedPlatformAdmin;

/// <summary>
/// Created once: when any platform admin exists nothing happens. An email that belongs to another user fails with
/// <c>Auth.EmailTaken</c> (the user is not promoted); concurrent seeds are decided by the unique email index.
/// </summary>
public sealed class SeedPlatformAdminCommandHandler(IPlatformAdminAccounts admins, IClock clock)
    : IRequestHandler<SeedPlatformAdminCommand, Result<SeedPlatformAdminOutcome>>
{
    public async Task<Result<SeedPlatformAdminOutcome>> Handle(SeedPlatformAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await admins.AnyAsync(cancellationToken))
        {
            return SeedPlatformAdminOutcome.AlreadyExists;
        }

        var account = new NewUserAccount(request.Email.Trim(), request.FullName.Trim(), Phone: null, PreferredLanguage.Ar, clock.UtcNow);
        var created = await admins.CreateAsync(account, request.Password, cancellationToken);
        return created.IsFailure ? created.Error : new SeedPlatformAdminOutcome(true, created.Value);
    }
}
