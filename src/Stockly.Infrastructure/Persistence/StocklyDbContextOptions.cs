using Microsoft.EntityFrameworkCore;

namespace Stockly.Infrastructure.Persistence;

/// <summary>Provider settings shared by runtime wiring and the design-time factory, so both build the same model.</summary>
internal static class StocklyDbContextOptions
{
    /// <summary>
    /// Pinned explicitly: without it the snake_case plugin renamed the table at design time
    /// (<c>__ef_migrations_history</c>) but not at runtime (<c>__EFMigrationsHistory</c>), so <c>dotnet ef</c>
    /// and the app would each see an empty migration history.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(StocklyDbContext).Assembly.FullName)
                .MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention();
}
