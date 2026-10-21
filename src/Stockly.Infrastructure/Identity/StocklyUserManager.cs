using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stockly.Infrastructure.Persistence;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// Identity's <see cref="UserManager{TUser}"/> with lockout time read from <see cref="TimeProvider"/> (backed by
/// <c>IClock</c>). The stock 10.0 implementation reads <see cref="DateTimeOffset.UtcNow"/> directly in
/// <see cref="IsLockedOutAsync"/> and <see cref="AccessFailedAsync"/>; the failed-attempt count is also made atomic.
/// </summary>
internal sealed class StocklyUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger,
    TimeProvider timeProvider,
    StocklyDbContext db)
    : UserManager<ApplicationUser>(
        store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
{
    public override async Task<bool> IsLockedOutAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!await GetLockoutEnabledAsync(user))
        {
            return false;
        }

        var lockoutEnd = await GetLockoutEndDateAsync(user);
        return lockoutEnd >= timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Counts a failed attempt; the attempt that reaches the limit locks the account and resets the count. One
    /// <c>UPDATE</c> does it in the database: counted on the loaded copy, attempts made at the same moment all read the
    /// same count and all but one lost their write to the concurrency stamp, so N guesses counted once.
    /// </summary>
    public override async Task<IdentityResult> AccessFailedAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ThrowIfDisposed();

        var max = Options.Lockout.MaxFailedAccessAttempts;
        var lockedUntil = timeProvider.GetUtcNow().Add(Options.Lockout.DefaultLockoutTimeSpan);
        var stamp = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE users SET
                lockout_end = CASE WHEN access_failed_count + 1 >= {max} THEN {lockedUntil} ELSE lockout_end END,
                access_failed_count = CASE WHEN access_failed_count + 1 >= {max} THEN 0 ELSE access_failed_count + 1 END,
                concurrency_stamp = {stamp}
            WHERE id = {user.Id}
            """,
            CancellationToken);
        await db.Entry(user).ReloadAsync(CancellationToken);
        return IdentityResult.Success;
    }
}
