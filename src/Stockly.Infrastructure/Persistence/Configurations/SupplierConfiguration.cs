using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;
using Stockly.Domain.Suppliers;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(s => s.ShopId);
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.Name).IsRequired();
        builder.Property(s => s.NormalizedName).IsRequired();
        builder.Property(s => s.IsArchived).HasDefaultValue(false);

        builder.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);

        // SupplierRepository.FindIdByNormalizedNameAsync (scan job); dedupe on confirm. Also serves the shop_id FK.
        builder.HasIndex(s => new { s.ShopId, s.NormalizedName })
            .IsUnique()
            .HasFilter("NOT is_archived")
            .HasDatabaseName("ux_suppliers_shop_id_normalized_name");

        // Supplier fuzzy match (Sprint 6).
        builder.HasIndex(s => s.NormalizedName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_suppliers_normalized_name_trgm");
    }
}
