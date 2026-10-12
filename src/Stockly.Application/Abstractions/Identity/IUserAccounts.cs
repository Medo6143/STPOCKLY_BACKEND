using Stockly.Domain.Common;
using Stockly.Domain.Users;

namespace Stockly.Application.Abstractions.Identity;

/// <summary>User accounts and passwords. Infrastructure implements it with ASP.NET Core Identity.</summary>
public interface IUserAccounts
{
    /// <summary>
    /// Creates the user with a hashed password. Fails with <c>Auth.EmailTaken</c> when the email (case-insensitive)
    /// is registered, or <c>Validation.Failed</c> when Identity rejects the password.
    /// </summary>
    Task<Result<Guid>> CreateAsync(NewUserAccount account, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a login attempt. An account that is locked out is reported before the password is checked. On an
    /// existing account a wrong password counts as a failed attempt (enough of them lock it) and a right one resets the
    /// count. An unknown email is <see cref="CredentialStatus.Invalid"/>, costs one password hash and is never locked.
    /// </summary>
    Task<CredentialCheck> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task RecordSignInAsync(Guid userId, DateTimeOffset signedInAt, CancellationToken cancellationToken);
}

public sealed record NewUserAccount(
    string Email, string FullName, string? Phone, PreferredLanguage PreferredLanguage, DateTimeOffset CreatedAt);

public enum CredentialStatus
{
    Valid = 1,
    Invalid = 2,
    LockedOut = 3,
}

/// <summary>Outcome of a login attempt: the user when valid, the lockout end when locked out.</summary>
public sealed record CredentialCheck(CredentialStatus Status, UserAccount? User, DateTimeOffset? LockedOutUntil)
{
    public static readonly CredentialCheck Invalid = new(CredentialStatus.Invalid, null, null);

    public static CredentialCheck Valid(UserAccount user) => new(CredentialStatus.Valid, user, null);

    public static CredentialCheck LockedOut(DateTimeOffset until) => new(CredentialStatus.LockedOut, null, until);
}

public sealed record UserAccount(Guid Id, string Email, string FullName, string? Phone, PreferredLanguage PreferredLanguage);
