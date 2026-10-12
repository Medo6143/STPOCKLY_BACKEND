using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "barcode",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_products_shop_id_barcode",
                table: "products",
                columns: new[] { "shop_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL AND NOT is_archived");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_products_shop_id_barcode",
                table: "products");

            migrationBuilder.DropColumn(
                name: "barcode",
                table: "products");

            migrationBuilder.DropColumn(
                name: "category",
                table: "products");
        }
    }
}
