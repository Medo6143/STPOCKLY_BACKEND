using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Stock;

namespace Stockly.Infrastructure.Persistence;

public sealed class StocklyDbContext(DbContextOptions<StocklyDbContext> options, ICurrentTenant currentTenant)
    : DbContext(options)
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(StocklyDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(ApplyTenantFilter)} not found.");

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Read by the query filters on every query, so each context instance filters by its own tenant.
    private Guid? CurrentShopId => currentTenant.ShopId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StocklyDbContext).Assembly);

        var tenantTypes = modelBuilder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(ITenantEntity).IsAssignableFrom(clrType))
            .ToList();

        foreach (var tenantType in tenantTypes)
        {
            ApplyTenantFilterMethod.MakeGenericMethod(tenantType).Invoke(this, [modelBuilder]);
        }
    }

    // ADR-0002, fail closed: without a current tenant the filter matches no rows.
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => CurrentShopId != null && e.ShopId == CurrentShopId);
}
