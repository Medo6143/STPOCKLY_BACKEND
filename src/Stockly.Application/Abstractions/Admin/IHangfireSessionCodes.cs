namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// One-time codes that open the Hangfire dashboard for a platform admin (contract §11.3): valid
/// <see cref="HangfireSession.CodeLifetime"/>, single use. Kept in the API process's memory: this assumes one API instance
/// (several instances would keep them in Redis instead).
/// </summary>
public interface IHangfireSessionCodes
{
    /// <summary>A new random code for <paramref name="adminUserId"/>.</summary>
    string Issue(Guid adminUserId);

    /// <summary>The admin the code was issued to, or <c>null</c> when it is unknown, expired or already used. Removes it.</summary>
    Guid? Redeem(string code);
}

/// <summary>Lifetimes of the Hangfire dashboard session (contract §11.3).</summary>
public static class HangfireSession
{
    /// <summary>How long an issued code can be redeemed.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    /// <summary>How long the dashboard cookie a code is exchanged for stays valid.</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(15);

    /// <summary>The dashboard's path; the session URL is this path with <c>?code=…</c>.</summary>
    public const string Path = "/hangfire";

    /// <summary>The query parameter carrying the one-time code.</summary>
    public const string CodeParameter = "code";
}
