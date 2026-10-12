namespace Stockly.Application.Abstractions.Identity;

/// <summary>
/// Lets registration write the tenant rows of a shop that has no token yet (ADR-0002, update 2026-10-12). While the
/// scope returned by <see cref="BeginForNewShop"/> is open, <see cref="ICurrentTenant"/> is that shop. Scoped per request. Only
/// <c>Features/Auth/Register</c> may use it (architecture test <c>TenantScopeUsageTests</c>).
/// </summary>
public interface ITenantScope
{
    IDisposable BeginForNewShop(Guid shopId);
}
