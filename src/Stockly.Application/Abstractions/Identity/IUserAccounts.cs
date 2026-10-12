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

    /// <summary>The user when the email exists and the password matches; otherwise <c>null</c> (no hint which one failed).</summary>
    Task<UserAccount?> FindByCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task RecordSignInAsync(Guid userId, DateTimeOffset signedInAt, CancellationToken cancellationToken);
}

public sealed record NewUserAccount(
    string Email, string FullName, string? Phone, PreferredLanguage PreferredLanguage, DateTimeOffset CreatedAt);

public sealed record UserAccount(Guid Id, string Email, string FullName, string? Phone, PreferredLanguage PreferredLanguage);
