using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Stock;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence;

/// <summary>
/// The application database. It is also the ASP.NET Core Identity user store (users, no roles tables), so user
/// writes share the command's transaction. <c>ShopMember</c> has no <c>DbSet</c> property on purpose: only
/// <c>ShopMembershipRepository</c> reads it (ADR-0002, update 2026-10-12).
/// </summary>
public sealed class StocklyDbContext(DbContextOptions<StocklyDbContext> options, ICurrentTenant currentTenant)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(StocklyDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(ApplyTenantFilter)} not found.");

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Read by the query filters on every query, so each context instance filters by its own tenant.
    private Guid? CurrentShopId => currentTenant.ShopId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's model first; Stockly's configurations then rename its tables and indexes.
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(StocklyDbContext).Assembly);

        var tenantTypes = builder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(ITenantEntity).IsAssignableFrom(clrType))
            .ToList();

        foreach (var tenantType in tenantTypes)
        {
            ApplyTenantFilterMethod.MakeGenericMethod(tenantType).Invoke(this, [builder]);
        }
    }

    // ADR-0002, fail closed: without a current tenant the filter matches no rows.
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => CurrentShopId != null && e.ShopId == CurrentShopId);
}
