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

        // InvoiceScan (erd.md §3.2, §3.3, §3.6, Sprint 2)
        "ux_suppliers_shop_id_normalized_name",
        "ix_suppliers_normalized_name_trgm",
        "ux_product_aliases_shop_id_supplier_id_normalized_alias",
        "ix_product_aliases_normalized_alias_trgm",
        "ux_invoices_shop_id_client_request_id",
        "ix_invoices_shop_id_created_at",
        "ix_invoices_shop_id_status_created_at",
        "ix_invoices_shop_id_image_sha256",
        "ix_invoices_shop_id_supplier_id_invoice_number",
        "ix_invoices_status_created_at_failed",
        "ux_invoice_items_invoice_id_line_no",
        "ix_invoice_items_shop_id_product_id",
        "ix_invoice_items_shop_id_expiry_date",
        "ix_invoice_items_created_product_id",
        "ix_ai_usage_records_shop_id_created_at",
        "ix_ai_usage_records_created_at",
        "ix_ai_usage_records_invoice_id",

        // Sales (erd.md §3.4, Sprint 4)
        "ux_sales_shop_id_client_request_id",
        "ix_sales_shop_id_created_at",
        "ux_sale_items_sale_id_line_no",
        "ix_sale_items_shop_id_product_id",

        // Platform admin (erd.md §3.1, §3.6, Sprint 5)
        "ix_shops_created_at",
        "ix_shops_normalized_name_trgm",
        "ix_users_normalized_email_pattern",
        "ix_admin_audit_logs_target_type_target_id_created_at",
        "ix_admin_audit_logs_admin_user_id_created_at",
        "ix_admin_audit_logs_created_at",

        // MatchingAndImageLifecycle (erd.md §3.2, §3.3, §3.5, Sprint 6)
        "ix_products_latin_key_trgm",
        "ix_product_aliases_latin_key_trgm",
        "ix_invoices_created_at_image_kept",
    ];

    private static readonly string[] ExpectedChecks =
    [
        "ck_products_low_stock_threshold",
        "ck_product_units_factor",
        "ck_stock_movements_quantity_change",
        "ck_stock_movements_single_source",
        "ck_product_aliases_times_confirmed",
        "ck_invoices_confirmed_at",
        "ck_invoice_items_product_or_new",
        "ck_invoice_items_new_product_complete",
        "ck_invoice_items_new_product_sell_price",
        "ck_invoice_items_unit_requires_product",
        "ck_invoice_items_confirmed_resolved",
        "ck_invoice_items_created_product",
        "ck_invoice_items_amounts",
        "ck_invoice_items_line_no",
        "ck_invoice_items_match_confidence",
        "ck_ai_usage_records_non_negative",
        "ck_sales_total_amount",
        "ck_sale_items_amounts",
        "ck_sale_items_line_no",
        "ck_shop_subscriptions_daily_scan_quota_override",
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
    public async Task Migrate_EmptyDatabase_CreatesEveryErdTable()
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
            "shop_subscriptions",
            "suppliers",
            "product_aliases",
            "invoices",
            "invoice_items",
            "ai_usage_records",
            "sales",
            "sale_items",
            "admin_audit_logs");
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
    [InlineData("ux_suppliers_shop_id_normalized_name", "WHERE (NOT is_archived)")]
    [InlineData("ix_invoices_shop_id_supplier_id_invoice_number", "WHERE (invoice_number IS NOT NULL)")]
    [InlineData("ix_invoices_status_created_at_failed", "WHERE (status = 4)")]
    [InlineData("ix_invoices_created_at_image_kept", "WHERE (image_deleted_at IS NULL)")]
    [InlineData("ix_invoice_items_created_product_id", "WHERE (created_product_id IS NOT NULL)")]
    [InlineData("ix_invoice_items_shop_id_expiry_date", "WHERE (expiry_date IS NOT NULL)")]
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

    [Theory]
    [InlineData("fk_stock_movements_invoices_invoice_id", "invoices")]
    [InlineData("fk_stock_movements_invoice_items_invoice_item_id", "invoice_items")]
    [InlineData("fk_stock_movements_sales_sale_id", "sales")]
    [InlineData("fk_stock_movements_sale_items_sale_item_id", "sale_items")]
    public async Task Migrate_StockMovementSourceColumns_AreNowForeignKeys(string constraint, string target)
    {
        var table = await fixture.ScalarAsync<string>(
            "SELECT confrelid::regclass::text FROM pg_constraint WHERE conname = @p0 AND contype = 'f'", constraint);

        table.Should().Be(target);
    }

    [Fact]
    public async Task Migrate_ProductAliasesUniqueIndex_TreatsNullSupplierAsEqual()
    {
        var definition = await fixture.ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_product_aliases_shop_id_supplier_id_normalized_alias'");

        definition.Should().EndWith("NULLS NOT DISTINCT");
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesTheHangfireSchema()
    {
        var count = await fixture.ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname = 'hangfire'");

        count.Should().Be(1, "Hangfire.PostgreSql creates its tables in it at startup (backend.md §12.4)");
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
    public async Task Migrate_LatinKeys_AreRequiredTextWithoutDefault()
    {
        var columns = await fixture.ColumnAsync(
            "SELECT concat_ws('|', table_name, data_type, is_nullable, coalesce(column_default, 'none')) FROM information_schema.columns " +
            "WHERE column_name = 'latin_key' AND table_schema = 'public' ORDER BY table_name");

        columns.Should().Equal("product_aliases|text|NO|none", "products|text|NO|none");
    }

    [Theory]
    [InlineData("ix_products_latin_key_trgm")]
    [InlineData("ix_product_aliases_latin_key_trgm")]
    public async Task Migrate_LatinKeyIndexes_AreGinTrigram(string index)
    {
        var definition = await fixture.ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = @p0", index);

        definition.Should().Contain("USING gin").And.EndWith("(latin_key gin_trgm_ops)");
    }

    [Fact]
    public async Task Migrate_Invoices_HasNullableImageDeletedAt()
    {
        var column = await fixture.ScalarAsync<string>(
            "SELECT concat_ws('|', data_type, is_nullable) FROM information_schema.columns " +
            "WHERE table_name = 'invoices' AND column_name = 'image_deleted_at'");

        column.Should().Be("timestamp with time zone|YES");
    }

    [Fact]
    public async Task Migrate_Products_DoesNotCreateXminColumn()
    {
        var count = await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'products' AND column_name = 'xmin'");

        count.Should().Be(0, "xmin is a PostgreSQL system column used as the concurrency token");
    }
}
