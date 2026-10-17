using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Platform admin accounts (contract §11.1). An admin is a user with <c>platform_role = PlatformAdmin</c>; there is no
/// admin registration endpoint, only the start-up seed. Used only by <c>Application/Features/Admin</c> (ADR-0005).
/// </summary>
public interface IPlatformAdminAccounts
{
    /// <summary>Whether any platform admin exists (the seed runs only when none does).</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Whether the user exists and is a platform admin.</summary>
    Task<bool> IsPlatformAdminAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a platform admin with a hashed password. Fails with <c>Auth.EmailTaken</c> when the email belongs to any
    /// user (an existing user is never promoted), or <c>Validation.Failed</c> when Identity rejects the password.
    /// </summary>
    Task<Result<Guid>> CreateAsync(NewUserAccount account, string password, CancellationToken cancellationToken);
}
