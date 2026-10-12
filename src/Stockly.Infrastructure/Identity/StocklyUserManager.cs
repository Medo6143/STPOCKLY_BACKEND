using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// Identity's <see cref="UserManager{TUser}"/> with lockout time read from <see cref="TimeProvider"/> (backed by
/// <c>IClock</c>). The stock 10.0 implementation reads <see cref="DateTimeOffset.UtcNow"/> directly in
/// <see cref="IsLockedOutAsync"/> and <see cref="AccessFailedAsync"/>; both are otherwise unchanged.
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
    TimeProvider timeProvider)
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

    /// <summary>Counts a failed attempt; the attempt that reaches the limit locks the account and resets the count.</summary>
    public override async Task<IdentityResult> AccessFailedAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ThrowIfDisposed();

        var lockoutStore = LockoutStore();
        var count = await lockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken);
        if (count >= Options.Lockout.MaxFailedAccessAttempts)
        {
            await lockoutStore.SetLockoutEndDateAsync(
                user, timeProvider.GetUtcNow().Add(Options.Lockout.DefaultLockoutTimeSpan), CancellationToken);
            await lockoutStore.ResetAccessFailedCountAsync(user, CancellationToken);
        }

        return await UpdateUserAsync(user);
    }

    private IUserLockoutStore<ApplicationUser> LockoutStore() =>
        Store as IUserLockoutStore<ApplicationUser>
        ?? throw new NotSupportedException("The user store does not support lockout.");
}
