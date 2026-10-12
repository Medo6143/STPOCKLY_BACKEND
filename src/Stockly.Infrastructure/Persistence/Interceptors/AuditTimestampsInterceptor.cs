using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Persistence.Interceptors;

/// <summary>Sets the <see cref="ShadowProperties.UpdatedAt"/> shadow property from <see cref="IClock"/> on insert and update.</summary>
public sealed class AuditTimestampsInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Metadata.FindProperty(ShadowProperties.UpdatedAt) is not null)
            {
                entry.Property(ShadowProperties.UpdatedAt).CurrentValue = now;
            }
        }
    }
}
