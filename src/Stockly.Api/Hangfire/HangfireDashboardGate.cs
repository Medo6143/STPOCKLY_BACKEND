using System.Globalization;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.DataProtection;
using Stockly.Api.Errors;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Infrastructure.Jobs;

namespace Stockly.Api.Hangfire;

/// <summary>
/// The Hangfire dashboard at <c>/hangfire</c>, for platform admins only (ADR-0005, contract §11.3). The browser cannot
/// send the admin's bearer token with the dashboard's own requests, so: <c>/hangfire?code=…</c> redeems a one-time code
/// from <c>POST /api/admin/hangfire/session</c> for an HttpOnly, <c>SameSite=Strict</c> cookie scoped to <c>/hangfire</c>,
/// valid 15 minutes, and redirects to the dashboard; every other request needs that cookie, else
/// <c>401 Auth.Unauthenticated</c>. The cookie holds the admin id and its expiry, encrypted with ASP.NET Core Data
/// Protection. The dashboard itself is built on the first authorized request, so start-up never waits for PostgreSQL.
/// </summary>
public sealed class HangfireDashboardGate(
    RequestDelegate next,
    HangfireStorage storage,
    IHangfireSessionCodes codes,
    IDataProtectionProvider protection,
    IClock clock)
{
    public const string CookieName = "stockly_hangfire";

    private const string ProtectorPurpose = "Stockly.Hangfire.Dashboard.v1";
    private const char Separator = '|';

    private readonly IDataProtector _protector = protection.CreateProtector(ProtectorPurpose);
    private readonly Lock _gate = new();
    private AspNetCoreDashboardMiddleware? _dashboard;

    public async Task InvokeAsync(HttpContext context, ProblemWriter problems)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problems);

        if (context.Request.Query.TryGetValue(HangfireSession.CodeParameter, out var code))
        {
            await RedeemAsync(context, problems, code.ToString());
            return;
        }

        if (!HasValidCookie(context))
        {
            await problems.WriteAsync(context, StatusCodes.Status401Unauthorized, AuthErrors.Unauthenticated.Code);
            return;
        }

        await Dashboard().Invoke(context);
    }

    private async Task RedeemAsync(HttpContext context, ProblemWriter problems, string code)
    {
        if (codes.Redeem(code) is not { } adminUserId)
        {
            await problems.WriteAsync(context, StatusCodes.Status401Unauthorized, AuthErrors.Unauthenticated.Code);
            return;
        }

        var expiresAt = clock.UtcNow + HangfireSession.CookieLifetime;
        var value = string.Create(CultureInfo.InvariantCulture, $"{adminUserId:D}{Separator}{expiresAt.ToUnixTimeSeconds()}");
        context.Response.Cookies.Append(CookieName, _protector.Protect(value), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = HangfireSession.Path,
            Expires = expiresAt,
            IsEssential = true,
        });

        // Drop the used code from the address bar.
        context.Response.Redirect(context.Request.PathBase + "/");
    }

    private bool HasValidCookie(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        string value;
        try
        {
            value = _protector.Unprotect(cookie);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }

        var parts = value.Split(Separator);
        return parts.Length == 2
            && Guid.TryParse(parts[0], out _)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            && clock.UtcNow.ToUnixTimeSeconds() < expiresAt;
    }

    // Authorization is this gate's job; the dashboard's own filters are emptied (its default allows local requests only).
    private AspNetCoreDashboardMiddleware Dashboard()
    {
        if (_dashboard is { } dashboard)
        {
            return dashboard;
        }

        lock (_gate)
        {
            return _dashboard ??= new AspNetCoreDashboardMiddleware(
                next,
                storage.Get(),
                new DashboardOptions { Authorization = [], AsyncAuthorization = [], DisplayStorageConnectionString = false },
                DashboardRoutes.Routes);
        }
    }
}

/// <summary>Maps <see cref="HangfireDashboardGate"/> at <see cref="HangfireSession.Path"/>.</summary>
public static class HangfireDashboardSetup
{
    public static IApplicationBuilder MapStocklyHangfireDashboard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Map(HangfireSession.Path, branch => branch.UseMiddleware<HangfireDashboardGate>());
    }
}
