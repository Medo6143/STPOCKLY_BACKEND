using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public const string TableName = "products";

    public const string BarcodeIndex = "ux_products_shop_id_barcode";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(TableName, table =>
            table.HasCheckConstraint("ck_products_low_stock_threshold", "low_stock_threshold >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(p => p.ShopId);
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.NormalizedName).IsRequired();
        builder.Property(p => p.LatinKey).IsRequired();
        builder.Property(p => p.Barcode);
        builder.Property(p => p.Category);
        builder.Property(p => p.Quantity).HasPrecision(14, 3);
        builder.Property(p => p.CostPrice).HasPrecision(18, 2);
        builder.Property(p => p.LowStockThreshold).HasPrecision(14, 3).HasDefaultValue(0m);
        builder.Property<DateTimeOffset>(ShadowProperties.UpdatedAt);
        builder.Property<uint>(ShadowProperties.RowVersion).IsRowVersion();

        builder.Ignore(p => p.BaseUnit);
        builder.Ignore(p => p.IsLowStock);

        builder.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);

        // Cascade lets EF delete a unit removed from the aggregate. Products themselves are never hard-deleted.
        builder.HasMany(p => p.Units).WithOne().HasForeignKey(u => u.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Units).HasField("_units").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Barcode scan in the sale screen; P3/P4 reject a barcode used by another active product (erd.md §3.2).
        builder.HasIndex(p => new { p.ShopId, p.Barcode })
            .IsUnique()
            .HasFilter("barcode IS NOT NULL AND NOT is_archived")
            .HasDatabaseName(BarcodeIndex);

        // Product list sorted by name, paging (erd.md §3.2).
        builder.HasIndex(p => new { p.ShopId, p.NormalizedName })
            .HasFilter("NOT is_archived")
            .HasDatabaseName("ix_products_shop_id_normalized_name");

        // Search box, fuzzy match (Sprint 6).
        builder.HasIndex(p => p.NormalizedName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_products_normalized_name_trgm");

        // Cross-script fuzzy match: ProductSimilarityQueries, latin_key % @latin (erd.md §3.2).
        builder.HasIndex(p => p.LatinKey)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_products_latin_key_trgm");

        // GetLowStockQuery: only low items live in this partial index.
        builder.HasIndex(p => p.ShopId)
            .HasFilter("quantity <= low_stock_threshold AND NOT is_archived")
            .HasDatabaseName("ix_products_low_stock");
    }
}
