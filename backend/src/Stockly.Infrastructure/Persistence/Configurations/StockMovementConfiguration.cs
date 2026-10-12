using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;
using Stockly.Domain.Stock;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_quantity_change", "quantity_change <> 0");
            table.HasCheckConstraint("ck_stock_movements_single_source", "num_nonnulls(invoice_item_id, sale_item_id) <= 1");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // Every StockMovement property is get-only (append-only row); map them explicitly.
        builder.Property(m => m.ShopId);
        builder.Property(m => m.ProductId);
        builder.Property(m => m.InvoiceId);
        builder.Property(m => m.InvoiceItemId);
        builder.Property(m => m.SaleId);
        builder.Property(m => m.SaleItemId);
        builder.Property(m => m.Note);
        builder.Property(m => m.CreatedByUserId);
        builder.Property(m => m.CreatedAt);
        builder.Property(m => m.QuantityChange).HasPrecision(14, 3);
        builder.Property(m => m.BalanceAfter).HasPrecision(14, 3);
        builder.Property(m => m.Reason).HasConversion<short>();

        // invoice_id, invoice_item_id, sale_id, sale_item_id and created_by_user_id are plain columns until their
        // target tables exist; see "Deferred foreign keys" in docs/erd.md.
        builder.HasOne<Shop>().WithMany().HasForeignKey(m => m.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Idempotent confirm; one terminal reversal per line (erd.md §3.5, ADR-0001).
        builder.HasIndex(m => new { m.InvoiceItemId, m.Reason })
            .IsUnique()
            .HasFilter("invoice_item_id IS NOT NULL")
            .HasDatabaseName("ux_stock_movements_invoice_item_id_reason");

        // Idempotent sale.
        builder.HasIndex(m => m.SaleItemId)
            .IsUnique()
            .HasFilter("sale_item_id IS NOT NULL")
            .HasDatabaseName("ux_stock_movements_sale_item_id");

        // Product movement history.
        builder.HasIndex(m => new { m.ShopId, m.ProductId, m.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_stock_movements_shop_id_product_id_created_at");

        // Dashboard activity feed.
        builder.HasIndex(m => new { m.ShopId, m.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_stock_movements_shop_id_created_at");
    }
}
