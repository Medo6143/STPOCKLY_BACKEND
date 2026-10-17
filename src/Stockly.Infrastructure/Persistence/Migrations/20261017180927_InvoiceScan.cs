using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hangfire's tables live here. Hangfire.PostgreSql creates and upgrades them at startup, because their DDL
            // belongs to the Hangfire version (docs/design/backend.md §17.1 item 8, documented exception to rule 31).
            migrationBuilder.EnsureSchema(
                name: "hangfire");

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_suppliers_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    image_object_key = table.Column<string>(type: "text", nullable: false),
                    image_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name_raw = table.Column<string>(type: "text", nullable: true),
                    invoice_number = table.Column<string>(type: "text", nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    computed_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    warnings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    raw_ai_json = table.Column<string>(type: "jsonb", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_confirmed_at", "confirmed_at IS NULL OR status = 3");
                    table.ForeignKey(
                        name: "fk_invoices_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias_text = table.Column<string>(type: "text", nullable: false),
                    normalized_alias = table.Column<string>(type: "text", nullable: false),
                    times_confirmed = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_aliases", x => x.id);
                    table.CheckConstraint("ck_product_aliases_times_confirmed", "times_confirmed > 0");
                    table.ForeignKey(
                        name: "fk_product_aliases_product_units_product_unit_id",
                        column: x => x.product_unit_id,
                        principalTable: "product_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_aliases_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_aliases_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_aliases_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(14,8)", precision: 14, scale: 8, nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_records", x => x.id);
                    table.CheckConstraint("ck_ai_usage_records_non_negative", "prompt_tokens >= 0 AND completion_tokens >= 0 AND cost_usd >= 0 AND latency_ms >= 0");
                    table.ForeignKey(
                        name: "fk_ai_usage_records_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_usage_records_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    raw_name = table.Column<string>(type: "text", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_product_name = table.Column<string>(type: "text", nullable: true),
                    new_product_base_unit_name = table.Column<string>(type: "text", nullable: true),
                    new_product_sell_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    batch_no = table.Column<string>(type: "text", nullable: true),
                    match_confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_items", x => x.id);
                    table.CheckConstraint("ck_invoice_items_amounts", "quantity > 0 AND unit_price >= 0 AND line_total >= 0");
                    table.CheckConstraint("ck_invoice_items_confirmed_resolved", "confirmed_at IS NULL OR num_nonnulls(product_id, new_product_name) = 1");
                    table.CheckConstraint("ck_invoice_items_created_product", "(created_product_id IS NOT NULL) = (new_product_name IS NOT NULL AND confirmed_at IS NOT NULL)");
                    table.CheckConstraint("ck_invoice_items_line_no", "line_no BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_invoice_items_match_confidence", "match_confidence IS NULL OR match_confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_invoice_items_new_product_complete", "num_nonnulls(new_product_name, new_product_base_unit_name, new_product_sell_price) IN (0, 3)");
                    table.CheckConstraint("ck_invoice_items_new_product_sell_price", "new_product_sell_price IS NULL OR new_product_sell_price >= 0");
                    table.CheckConstraint("ck_invoice_items_product_or_new", "product_id IS NULL OR new_product_name IS NULL");
                    table.CheckConstraint("ck_invoice_items_unit_requires_product", "(product_id IS NULL) = (product_unit_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_product_units_product_unit_id",
                        column: x => x.product_unit_id,
                        principalTable: "product_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_products_created_product_id",
                        column: x => x.created_product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_invoice_id",
                table: "stock_movements",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_created_at",
                table: "ai_usage_records",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_invoice_id",
                table: "ai_usage_records",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_shop_id_created_at",
                table: "ai_usage_records",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_created_product_id",
                table: "invoice_items",
                column: "created_product_id",
                filter: "created_product_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_product_id",
                table: "invoice_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_product_unit_id",
                table: "invoice_items",
                column: "product_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_shop_id_expiry_date",
                table: "invoice_items",
                columns: new[] { "shop_id", "expiry_date" },
                filter: "expiry_date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_shop_id_product_id",
                table: "invoice_items",
                columns: new[] { "shop_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoice_items_invoice_id_line_no",
                table: "invoice_items",
                columns: new[] { "invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_confirmed_by_user_id",
                table: "invoices",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_by_user_id",
                table: "invoices",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_shop_id_created_at",
                table: "invoices",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_shop_id_image_sha256",
                table: "invoices",
                columns: new[] { "shop_id", "image_sha256" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_shop_id_status_created_at",
                table: "invoices",
                columns: new[] { "shop_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_shop_id_supplier_id_invoice_number",
                table: "invoices",
                columns: new[] { "shop_id", "supplier_id", "invoice_number" },
                filter: "invoice_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_created_at_failed",
                table: "invoices",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true },
                filter: "status = 4");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_supplier_id",
                table: "invoices",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_shop_id_client_request_id",
                table: "invoices",
                columns: new[] { "shop_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_aliases_normalized_alias_trgm",
                table: "product_aliases",
                column: "normalized_alias")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_product_aliases_product_id",
                table: "product_aliases",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_aliases_product_unit_id",
                table: "product_aliases",
                column: "product_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_aliases_supplier_id",
                table: "product_aliases",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_aliases_shop_id_supplier_id_normalized_alias",
                table: "product_aliases",
                columns: new[] { "shop_id", "supplier_id", "normalized_alias" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_normalized_name_trgm",
                table: "suppliers",
                column: "normalized_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_shop_id_normalized_name",
                table: "suppliers",
                columns: new[] { "shop_id", "normalized_name" },
                unique: true,
                filter: "NOT is_archived");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_invoice_items_invoice_item_id",
                table: "stock_movements",
                column: "invoice_item_id",
                principalTable: "invoice_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_invoices_invoice_id",
                table: "stock_movements",
                column: "invoice_id",
                principalTable: "invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_invoice_items_invoice_item_id",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_invoices_invoice_id",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "ai_usage_records");

            migrationBuilder.DropTable(
                name: "invoice_items");

            migrationBuilder.DropTable(
                name: "product_aliases");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_invoice_id",
                table: "stock_movements");

            // Removes Hangfire's tables (and queued jobs) with their schema: they cannot outlive the migration that made room for them.
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS hangfire CASCADE;");
        }
    }
}
