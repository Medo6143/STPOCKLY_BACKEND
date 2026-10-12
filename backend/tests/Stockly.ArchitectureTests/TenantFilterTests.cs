using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Domain.Common;
using Stockly.Infrastructure.Persistence;

namespace Stockly.ArchitectureTests;

/// <summary>ADR-0002: every tenant entity in the EF model has a query filter. Builds the real model; never connects.</summary>
public sealed class TenantFilterTests
{
    [Fact]
    public void EfModel_EveryTenantEntity_HasQueryFilter()
    {
        using var context = CreateModelOnlyContext();

        var tenantTypes = context.Model.GetEntityTypes()
            .Where(entityType => typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            .ToList();
        var unfiltered = tenantTypes
            .Where(entityType => entityType.GetQueryFilter() is null)
            .Select(entityType => entityType.ClrType.Name);

        tenantTypes.Should().NotBeEmpty("the model maps tenant entities, so this rule must not pass vacuously");
        unfiltered.Should().BeEmpty("every ITenantEntity must be filtered by the current tenant (ADR-0002)");
    }

    private static StocklyDbContext CreateModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<StocklyDbContext>()
            .UseNpgsql("Host=model-only")
            .Options;
        return new StocklyDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? ShopId => null;
    }
}
