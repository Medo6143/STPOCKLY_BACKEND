using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Domain.Common;
using Stockly.Infrastructure.Persistence;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// <see cref="IUserAccounts"/> on ASP.NET Core Identity. Uses the request's <see cref="StocklyDbContext"/>, so user
/// writes join the command's transaction. Never logs emails or passwords.
/// </summary>
internal sealed class IdentityUserAccounts(UserManager<ApplicationUser> userManager, StocklyDbContext db) : IUserAccounts
{
    private const string PasswordField = "password";
    private const string PasswordErrorPrefix = "Password";

    // Verified when the email is unknown, so both login failures cost one password hash.
    private static readonly Lazy<string> UnknownUserHash =
        new(() => new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public async Task<Result<Guid>> CreateAsync(NewUserAccount account, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = account.Email,
            Email = account.Email,
            FullName = account.FullName,
            PhoneNumber = account.Phone,
            PreferredLanguage = account.PreferredLanguage,
            CreatedAt = account.CreatedAt,
        };

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two registrations with the same email at once: the unique index decides.
            return AuthErrors.EmailTaken;
        }

        if (result.Succeeded)
        {
            return user.Id;
        }

        var codes = result.Errors.Select(error => error.Code).ToList();
        if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateEmail)) || codes.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return AuthErrors.EmailTaken;
        }

        if (codes.TrueForAll(code => code.StartsWith(PasswordErrorPrefix, StringComparison.Ordinal)))
        {
            return ValidationFailure.WithFieldErrors(
                new Dictionary<string, string[]> { [PasswordField] = [ValidationMessages.PasswordRejected] });
        }

        throw new InvalidOperationException($"Identity rejected a new user: {string.Join(", ", codes)}.");
    }

    public async Task<CredentialCheck> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), UnknownUserHash.Value, password);
            return CredentialCheck.Invalid;
        }

        // Checked before the password: while locked, even the right password is refused.
        if (await userManager.IsLockedOutAsync(user))
        {
            return CredentialCheck.LockedOut(user.LockoutEnd!.Value);
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return CredentialCheck.Invalid;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return CredentialCheck.Valid(ToAccount(user));
    }

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAccount(user.Id, user.Email!, user.FullName, user.PhoneNumber, user.PreferredLanguage))
            .SingleOrDefaultAsync(cancellationToken);

    public Task RecordSignInAsync(Guid userId, DateTimeOffset signedInAt, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastLoginAt, signedInAt), cancellationToken);

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.Email!, user.FullName, user.PhoneNumber, user.PreferredLanguage);
}
