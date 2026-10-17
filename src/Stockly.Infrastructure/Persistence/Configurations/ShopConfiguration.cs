using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public const string TableName = "shops";

    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.Name).IsRequired();
        builder.Property(s => s.NormalizedName).IsRequired();
        builder.Property(s => s.ShopType).HasConversion<short>();
        builder.Property(s => s.Status).HasConversion<short>();

        // Admin shop list AD4 with a status filter (erd.md §3.1).
        builder.HasIndex(s => new { s.Status, s.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_shops_status_created_at");

        // Admin shop list AD4 without a status filter.
        builder.HasIndex(s => s.CreatedAt)
            .IsDescending()
            .HasDatabaseName("ix_shops_created_at");

        // AD4 search: ILIKE '%term%' on the normalized name.
        builder.HasIndex(s => s.NormalizedName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_shops_normalized_name_trgm");
    }
}
