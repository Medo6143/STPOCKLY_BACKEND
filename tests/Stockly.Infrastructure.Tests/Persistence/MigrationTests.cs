using Microsoft.EntityFrameworkCore;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class MigrationTests(PostgresFixture fixture)
{
    private static readonly string[] ExpectedIndexes =
    [
        // InitialCatalog (erd.md §3.1, §3.2, §3.5)
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

        // Identity (erd.md §3.1)
        "ux_users_normalized_email",
        "ux_users_normalized_user_name",
        "ux_refresh_tokens_token_hash",
        "ix_refresh_tokens_user_id_active",
        "ix_refresh_tokens_family_id_active",
        "ix_shop_members_user_id_active",
        "ux_shop_members_shop_id_owner",
        "ux_plans_code",
        "ux_shop_subscriptions_shop_id_active",
        "ix_shop_subscriptions_ends_on_active",

        // ProductsCatalog (erd.md §3.2, step 1.6)
        "ux_products_shop_id_barcode",
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
    public async Task Model_HasNoChangesMissingAMigration()
    {
        await using var context = fixture.CreateContext(shopId: null);

        context.Database.HasPendingModelChanges().Should().BeFalse("every model change ships with its migration (AGENTS.md rule 31)");
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

        tables.Should().BeEquivalentTo(
            "shops",
            "products",
            "product_units",
            "stock_movements",
            "users",
            "user_claims",
            "user_logins",
            "user_tokens",
            "refresh_tokens",
            "shop_members",
            "plans",
            "shop_subscriptions");
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesEveryErdIndex()
    {
        var indexes = await fixture.ColumnAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        indexes.Should().Contain(ExpectedIndexes);
    }

    [Theory]
    [InlineData("ix_refresh_tokens_user_id_active", "WHERE (revoked_at IS NULL)")]
    [InlineData("ix_refresh_tokens_family_id_active", "WHERE (revoked_at IS NULL)")]
    [InlineData("ix_shop_members_user_id_active", "WHERE is_active")]
    [InlineData("ux_shop_members_shop_id_owner", "WHERE (role = 1)")]
    [InlineData("ux_shop_subscriptions_shop_id_active", "WHERE (status = 1)")]
    [InlineData("ix_shop_subscriptions_ends_on_active", "WHERE (status = 1)")]
    [InlineData("ux_products_shop_id_barcode", "WHERE ((barcode IS NOT NULL) AND (NOT is_archived))")]
    public async Task Migrate_PartialIndexes_HaveTheErdPredicate(string index, string predicate)
    {
        var definition = await fixture.ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = @p0", index);

        definition.Should().EndWith(predicate);
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesEveryErdCheckConstraint()
    {
        var checks = await fixture.ColumnAsync(
            "SELECT conname FROM pg_constraint WHERE contype = 'c' AND connamespace = 'public'::regnamespace");

        checks.Should().BeEquivalentTo(ExpectedChecks);
    }

    [Fact]
    public async Task Migrate_StockMovementsCreatedBy_IsNowAForeignKeyToUsers()
    {
        var target = await fixture.ScalarAsync<string>(
            "SELECT confrelid::regclass::text FROM pg_constraint WHERE conname = 'fk_stock_movements_users_created_by_user_id' AND contype = 'f'");

        target.Should().Be("users");
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_SeedsTheDefaultPlan()
    {
        var plan = await fixture.ColumnAsync(
            "SELECT concat_ws('|', id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) FROM plans");

        plan.Should().Equal(
            $"{DefaultPlan.Id}|standard|الأساسية|Standard|30|3|0.00|t");
    }

    [Fact]
    public async Task Migrate_RuntimeAndDesignTime_UseSameHistoryTable()
    {
        var historyTables = await fixture.ColumnAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name ILIKE '%migrations%history%'");

        historyTables.Should().Equal("__ef_migrations_history");
    }

    [Fact]
    public async Task Migrate_Products_HasNullableTextBarcodeAndCategory()
    {
        var columns = await fixture.ColumnAsync(
            "SELECT concat_ws('|', column_name, data_type, is_nullable) FROM information_schema.columns " +
            "WHERE table_name = 'products' AND column_name IN ('barcode', 'category') ORDER BY column_name");

        columns.Should().Equal("barcode|text|YES", "category|text|YES");
    }

    [Fact]
    public async Task Migrate_Products_DoesNotCreateXminColumn()
    {
        var count = await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'products' AND column_name = 'xmin'");

        count.Should().Be(0, "xmin is a PostgreSQL system column used as the concurrency token");
    }
}
