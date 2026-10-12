namespace Stockly.Application.Features.Auth;

/// <summary>Brute-force protection values from the API contract (§1.4, v1.4). The only place they are defined.</summary>
public static class AuthProtection
{
    /// <summary>Consecutive failed logins on an existing account that lock it.</summary>
    public const int MaxFailedLoginAttempts = 5;

    /// <summary>How long an account stays locked after <see cref="MaxFailedLoginAttempts"/> failures.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
