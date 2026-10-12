using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>One real PostgreSQL per test collection, migrated once with the production migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Must equal the postgres image in docker-compose.yml (guarded by <c>ComposeImageTests</c>).</summary>
    public const string PostgresImage =
        "postgres:16.15-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly List<ServiceProvider> _providers = [];

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext(shopId: null);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>Builds a context through the production wiring (<c>AddPersistence</c>) for the given tenant.</summary>
    public StocklyDbContext CreateContext(Guid? shopId, IClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddPersistence(ConnectionString);
        services.AddSingleton<ICurrentTenant>(new FixedTenant(shopId));
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider.CreateScope().ServiceProvider.GetRequiredService<StocklyDbContext>();
    }

    public async Task<Shop> CreateShopAsync(string name = "صيدلية الاختبار")
    {
        var shop = Shop.Create(name, ShopType.Pharmacy, null, null, null, TestTime.T0).Value;
        await using var context = CreateContext(shopId: null);
        context.Shops.Add(shop);
        await context.SaveChangesAsync();
        return shop;
    }

    public async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
        {
            command.Parameters.AddWithValue($"p{index}", value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T?)await command.ExecuteScalarAsync();
    }

    public async Task<List<string>> ColumnAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed class FixedTenant(Guid? shopId) : ICurrentTenant
    {
        public Guid? ShopId => shopId;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
