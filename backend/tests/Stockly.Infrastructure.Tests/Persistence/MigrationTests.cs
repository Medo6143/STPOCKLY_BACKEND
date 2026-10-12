using Microsoft.EntityFrameworkCore;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class MigrationTests(PostgresFixture fixture)
{
    private static readonly string[] ExpectedIndexes =
    [
        "ix_shops_status_created_at",
        "ix_products_shop_id_normalized_name",
        "ix_products_normalized_name_trgm",
        "ix_products_low_stock",
        "ux_product_units_product_id_name",
        "ux_product_units_product_id_base",
        "ux_product_units_shop_id_barcode",
        "ux_stock_movements_invoice_item_id_reason",
        "ux_stock_movements_sale_item_id",
        "ix_stock_movements_shop_id_product_id_created_at",
        "ix_stock_movements_shop_id_created_at",
    ];

    private static readonly string[] ExpectedChecks =
    [
        "ck_products_low_stock_threshold",
        "ck_product_units_factor",
        "ck_stock_movements_quantity_change",
        "ck_stock_movements_single_source",
    ];

    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesEveryMigration()
    {
        await using var context = fixture.CreateContext(shopId: null);

        var pending = await context.Database.GetPendingMigrationsAsync();
        var applied = await context.Database.GetAppliedMigrationsAsync();

        pending.Should().BeEmpty();
        applied.Should().Equal(context.Database.GetMigrations());
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesPgTrgmExtension()
    {
        var count = await fixture.ScalarAsync<long>("SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'");

        count.Should().Be(1);
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesSprint1Tables()
    {
        var tables = await fixture.ColumnAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__ef_migrations_history'");

        tables.Should().BeEquivalentTo("shops", "products", "product_units", "stock_movements");
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesEveryErdIndex()
    {
        var indexes = await fixture.ColumnAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        indexes.Should().Contain(ExpectedIndexes);
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesEveryErdCheckConstraint()
    {
        var checks = await fixture.ColumnAsync(
            "SELECT conname FROM pg_constraint WHERE contype = 'c' AND connamespace = 'public'::regnamespace");

        checks.Should().BeEquivalentTo(ExpectedChecks);
    }

    [Fact]
    public async Task Migrate_RuntimeAndDesignTime_UseSameHistoryTable()
    {
        var historyTables = await fixture.ColumnAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name ILIKE '%migrations%history%'");

        historyTables.Should().Equal("__ef_migrations_history");
    }

    [Fact]
    public async Task Migrate_Products_DoesNotCreateXminColumn()
    {
        var count = await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'products' AND column_name = 'xmin'");

        count.Should().Be(0, "xmin is a PostgreSQL system column used as the concurrency token");
    }
}
