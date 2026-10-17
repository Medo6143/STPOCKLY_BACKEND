using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public const string ProductUnitForeignKey = "fk_sale_items_product_units_product_unit_id";

    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items", table =>
        {
            table.HasCheckConstraint(
                "ck_sale_items_amounts", "quantity > 0 AND list_unit_price >= 0 AND unit_price >= 0 AND line_total >= 0");
            table.HasCheckConstraint("ck_sale_items_line_no", $"line_no BETWEEN 1 AND {SaleLimits.MaxItems}");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(i => i.ShopId);
        builder.Property(i => i.SaleId);
        builder.Property(i => i.LineNo);
        builder.Property(i => i.ProductId);
        builder.Property(i => i.ProductUnitId);
        builder.Property(i => i.Quantity).HasPrecision(14, 3);
        builder.Property(i => i.ListUnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
        builder.Ignore(i => i.PriceOverridden);

        builder.HasOne<Shop>().WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(ProductUnitForeignKey);

        // Loading a sale's lines in order (SaleQueries); FK support for sale_id.
        builder.HasIndex(i => new { i.SaleId, i.LineNo })
            .IsUnique()
            .HasDatabaseName("ux_sale_items_sale_id_line_no");

        // Top sellers (docs/erd.md §3.4).
        builder.HasIndex(i => new { i.ShopId, i.ProductId })
            .HasDatabaseName("ix_sale_items_shop_id_product_id");
    }
}
