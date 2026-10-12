using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> builder)
    {
        builder.ToTable("product_units", table =>
            table.HasCheckConstraint("ck_product_units_factor", "factor > 0"));

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(u => u.ShopId);
        builder.Property(u => u.ProductId);
        builder.Property(u => u.IsBase);
        builder.Property(u => u.Name).IsRequired();
        builder.Property(u => u.Factor).HasPrecision(14, 6);
        builder.Property(u => u.SellPrice).HasPrecision(18, 2);

        builder.HasOne<Shop>().WithMany().HasForeignKey(u => u.ShopId).OnDelete(DeleteBehavior.Restrict);

        // No duplicate unit names; also serves loading units with the product (erd.md §3.2).
        builder.HasIndex(u => new { u.ProductId, u.Name })
            .IsUnique()
            .HasDatabaseName("ux_product_units_product_id_name");

        // Exactly one base unit per product.
        builder.HasIndex(u => u.ProductId)
            .IsUnique()
            .HasFilter("is_base")
            .HasDatabaseName("ux_product_units_product_id_base");

        // A box and a strip can have different barcodes; a barcode is unique in the shop.
        builder.HasIndex(u => new { u.ShopId, u.Barcode })
            .IsUnique()
            .HasFilter("barcode IS NOT NULL")
            .HasDatabaseName("ux_product_units_shop_id_barcode");
    }
}
