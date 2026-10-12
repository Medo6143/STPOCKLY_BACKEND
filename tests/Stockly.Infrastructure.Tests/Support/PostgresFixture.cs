using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Identity;
using Stockly.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>One real PostgreSQL per test collection, migrated once with the production migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Must equal the postgres image in docker-compose.yml (guarded by <c>ComposeImageTests</c>).</summary>
    public const string PostgresImage =
        "postgres:16.15-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea";

    /// <summary>Test-only value, 48 bytes. Never a real secret.</summary>
    public const string TestSigningKey = "test-only-signing-key-0123456789-abcdefghijklmno";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly List<ServiceProvider> _providers = [];

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var context = CreateContext(shopId: null))
        {
            await context.Database.MigrateAsync();
        }

        // stock_movements.created_by_user_id is a real FK since migration Identity: the catalog tests' user must exist.
        await CreateUserAsync(Catalog.UserId);
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

    /// <summary>
    /// A scope with the production wiring (<c>AddPersistence</c> and, optionally, <c>AddStocklyIdentity</c>) for the
    /// given tenant. Resolve repositories and the unit of work from it.
    /// </summary>
    public AsyncServiceScope CreateScope(Guid? shopId, bool withIdentity = false, IClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(ConnectionString);
        if (withIdentity)
        {
            services.AddStocklyIdentity(new JwtOptions { SigningKey = TestSigningKey });
        }

        services.AddSingleton<ICurrentTenant>(new FixedTenant(shopId));
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider.CreateAsyncScope();
    }

    /// <summary>Inserts a minimal Identity user row (raw SQL, no password).</summary>
    public async Task<Guid> CreateUserAsync(Guid? id = null)
    {
        var userId = id ?? Guid.CreateVersion7();
        var email = $"user-{userId:N}@example.com";
        await ExecuteAsync(
            "INSERT INTO users (id, full_name, preferred_language, platform_role, created_at, user_name, normalized_user_name, " +
            "email, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) " +
            "VALUES (@p0, 'Test User', 1, 0, now(), @p1, @p2, @p1, @p2, false, false, false, false, 0)",
            userId,
            email,
            email.ToUpperInvariant());
        return userId;
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

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
        {
            command.Parameters.AddWithValue($"p{index}", value);
        }

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
