using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ProductAliasConfiguration : IEntityTypeConfiguration<ProductAlias>
{
    public const string ProductUnitForeignKey = "fk_product_aliases_product_units_product_unit_id";

    public void Configure(EntityTypeBuilder<ProductAlias> builder)
    {
        builder.ToTable("product_aliases", table =>
            table.HasCheckConstraint("ck_product_aliases_times_confirmed", "times_confirmed > 0"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(a => a.ShopId);
        builder.Property(a => a.SupplierId);
        builder.Property(a => a.AliasText).IsRequired();
        builder.Property(a => a.NormalizedAlias).IsRequired();
        builder.Property(a => a.LatinKey).IsRequired();
        builder.Property(a => a.TimesConfirmed).HasDefaultValue(1);

        builder.HasOne<Shop>().WithMany().HasForeignKey(a => a.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(a => a.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(a => a.ProductUnitId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(ProductUnitForeignKey);

        // Exact alias match during matching (step 2b); upsert on confirm. A null supplier conflicts too (PG 15+).
        builder.HasIndex(a => new { a.ShopId, a.SupplierId, a.NormalizedAlias })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_product_aliases_shop_id_supplier_id_normalized_alias");

        // Fuzzy match (TrigramMatchStrategy): ProductSimilarityQueries, normalized_alias % @name.
        builder.HasIndex(a => a.NormalizedAlias)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_product_aliases_normalized_alias_trgm");

        // Cross-script fuzzy match: ProductSimilarityQueries, latin_key % @latin.
        builder.HasIndex(a => a.LatinKey)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_product_aliases_latin_key_trgm");
    }
}
