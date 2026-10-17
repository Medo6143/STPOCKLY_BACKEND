using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>invoice_items with the nine CHECK constraints of docs/design/backend.md §10.3.</summary>
internal sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items", table =>
        {
            table.HasCheckConstraint("ck_invoice_items_product_or_new", "product_id IS NULL OR new_product_name IS NULL");
            table.HasCheckConstraint(
                "ck_invoice_items_new_product_complete",
                "num_nonnulls(new_product_name, new_product_base_unit_name, new_product_sell_price) IN (0, 3)");
            table.HasCheckConstraint(
                "ck_invoice_items_new_product_sell_price", "new_product_sell_price IS NULL OR new_product_sell_price >= 0");
            table.HasCheckConstraint(
                "ck_invoice_items_unit_requires_product", "(product_id IS NULL) = (product_unit_id IS NULL)");
            table.HasCheckConstraint(
                "ck_invoice_items_confirmed_resolved", "confirmed_at IS NULL OR num_nonnulls(product_id, new_product_name) = 1");
            table.HasCheckConstraint(
                "ck_invoice_items_created_product",
                "(created_product_id IS NOT NULL) = (new_product_name IS NOT NULL AND confirmed_at IS NOT NULL)");
            table.HasCheckConstraint("ck_invoice_items_amounts", "quantity > 0 AND unit_price >= 0 AND line_total >= 0");
            table.HasCheckConstraint("ck_invoice_items_line_no", $"line_no BETWEEN 1 AND {InvoiceLimits.MaxLines}");
            table.HasCheckConstraint(
                "ck_invoice_items_match_confidence", "match_confidence IS NULL OR match_confidence BETWEEN 0 AND 1");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(i => i.ShopId);
        builder.Property(i => i.InvoiceId);
        builder.Property(i => i.LineNo);
        builder.Property(i => i.RawName).IsRequired();
        builder.Property(i => i.Quantity).HasPrecision(14, 3);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
        builder.Property(i => i.ExpiryDate);
        builder.Property(i => i.BatchNo);
        builder.Property(i => i.NewProductSellPrice).HasPrecision(18, 2);
        builder.Property(i => i.MatchConfidence).HasPrecision(3, 2);

        builder.HasOne<Shop>().WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.CreatedProductId).OnDelete(DeleteBehavior.Restrict);

        // Review screen ordered by line; stable edits. Also serves loading the lines of an invoice.
        builder.HasIndex(i => new { i.InvoiceId, i.LineNo })
            .IsUnique()
            .HasDatabaseName("ux_invoice_items_invoice_id_line_no");

        // Product purchase history (product_id = @p).
        builder.HasIndex(i => new { i.ShopId, i.ProductId })
            .HasDatabaseName("ix_invoice_items_shop_id_product_id");

        // Product purchase history for products created from an invoice (created_product_id = @p); FK support.
        builder.HasIndex(i => i.CreatedProductId)
            .HasFilter("created_product_id IS NOT NULL")
            .HasDatabaseName("ix_invoice_items_created_product_id");

        // "Expiring soon" dashboard card (Sprint 4).
        builder.HasIndex(i => new { i.ShopId, i.ExpiryDate })
            .HasFilter("expiry_date IS NOT NULL")
            .HasDatabaseName("ix_invoice_items_shop_id_expiry_date");
    }
}
