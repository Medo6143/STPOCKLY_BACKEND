using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Stockly.Application.Abstractions.Identity;

namespace Stockly.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to build the model for migrations. Never connects: <c>migrations add</c> and
/// <c>migrations script</c> work offline. <c>database update</c> takes the real connection string via
/// <c>--connection</c>.
/// </summary>
internal sealed class StocklyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<StocklyDbContext>
{
    private const string OfflineConnectionString = "Host=design-time-only;Database=stockly";

    public StocklyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<StocklyDbContext>();
        StocklyDbContextOptions.Configure(builder, OfflineConnectionString);
        return new StocklyDbContext(builder.Options, new NoTenant());
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? ShopId => null;
    }
}
