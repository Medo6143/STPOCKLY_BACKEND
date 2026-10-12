using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("shops");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.Name).IsRequired();
        builder.Property(s => s.ShopType).HasConversion<short>();
        builder.Property(s => s.Status).HasConversion<short>();

        // Admin shop list (erd.md §3.1).
        builder.HasIndex(s => new { s.Status, s.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_shops_status_created_at");
    }
}
