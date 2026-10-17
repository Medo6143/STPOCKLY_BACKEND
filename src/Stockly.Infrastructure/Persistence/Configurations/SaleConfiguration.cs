using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Sales;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public const string ClientRequestIndex = "ux_sales_shop_id_client_request_id";

    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales", table => table.HasCheckConstraint("ck_sales_total_amount", "total_amount >= 0"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(s => s.ShopId);
        builder.Property(s => s.ClientRequestId);
        builder.Property(s => s.CreatedByUserId);
        builder.Property(s => s.Note);
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Sale idempotency (ADR-0006): SaleQueries.FindByClientRequestIdAsync; the header insert of a concurrent repeat
        // waits here and loses (CreateSaleCommandHandler).
        builder.HasIndex(s => new { s.ShopId, s.ClientRequestId })
            .IsUnique()
            .HasDatabaseName(ClientRequestIndex);

        // S2 newest first with an optional created_at range; D1 today's sales.
        builder.HasIndex(s => new { s.ShopId, s.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_sales_shop_id_created_at");
    }
}
