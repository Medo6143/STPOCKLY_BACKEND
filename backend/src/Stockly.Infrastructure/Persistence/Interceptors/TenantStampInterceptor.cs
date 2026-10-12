using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;

namespace Stockly.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Enforces the tenant on every write (ADR-0002). Insert: an empty <c>ShopId</c> is stamped from the current
/// tenant; any other shop, or no tenant at all, throws. Update: changing <c>ShopId</c> throws.
/// </summary>
public sealed class TenantStampInterceptor(ICurrentTenant currentTenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enforce(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>())
        {
            var shopId = entry.Property(nameof(ITenantEntity.ShopId));
            switch (entry.State)
            {
                case EntityState.Added:
                    Stamp(entry, shopId);
                    break;
                case EntityState.Modified when shopId.IsModified && !Equals(shopId.OriginalValue, shopId.CurrentValue):
                    throw new TenantViolationException(
                        $"{entry.Metadata.ClrType.Name} {entry.Entity.ShopId}: ShopId cannot change after insert.");
                default:
                    break;
            }
        }
    }

    private void Stamp(EntityEntry<ITenantEntity> entry, PropertyEntry shopId)
    {
        var tenant = currentTenant.ShopId
            ?? throw new TenantViolationException($"Cannot insert {entry.Metadata.ClrType.Name} without a current tenant.");

        var value = (Guid)shopId.CurrentValue!;
        if (value == Guid.Empty)
        {
            shopId.CurrentValue = tenant;
        }
        else if (value != tenant)
        {
            throw new TenantViolationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} for shop {value} while the current tenant is {tenant}.");
        }
    }
}
