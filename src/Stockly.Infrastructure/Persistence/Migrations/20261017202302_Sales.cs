using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.CheckConstraint("ck_sales_total_amount", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_sales_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    list_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_items", x => x.id);
                    table.CheckConstraint("ck_sale_items_amounts", "quantity > 0 AND list_unit_price >= 0 AND unit_price >= 0 AND line_total >= 0");
                    table.CheckConstraint("ck_sale_items_line_no", "line_no BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_sale_items_product_units_product_unit_id",
                        column: x => x.product_unit_id,
                        principalTable: "product_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_sale_id",
                table: "stock_movements",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_product_id",
                table: "sale_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_product_unit_id",
                table: "sale_items",
                column: "product_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id_product_id",
                table: "sale_items",
                columns: new[] { "shop_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sale_items_sale_id_line_no",
                table: "sale_items",
                columns: new[] { "sale_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_created_by_user_id",
                table: "sales",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_created_at",
                table: "sales",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_sales_shop_id_client_request_id",
                table: "sales",
                columns: new[] { "shop_id", "client_request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_sale_items_sale_item_id",
                table: "stock_movements",
                column: "sale_item_id",
                principalTable: "sale_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_sales_sale_id",
                table: "stock_movements",
                column: "sale_id",
                principalTable: "sales",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_sale_items_sale_item_id",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_sales_sale_id",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "sale_items");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_sale_id",
                table: "stock_movements");
        }
    }
}
