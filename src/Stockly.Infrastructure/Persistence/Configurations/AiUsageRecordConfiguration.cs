using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public const string TableName = "ai_usage_records";

    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(
            "ck_ai_usage_records_non_negative",
            "prompt_tokens >= 0 AND completion_tokens >= 0 AND cost_usd >= 0 AND latency_ms >= 0"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Every property is get-only (append-only row); map them explicitly.
        builder.Property(r => r.ShopId);
        builder.Property(r => r.InvoiceId);
        builder.Property(r => r.Model).IsRequired();
        builder.Property(r => r.PromptTokens);
        builder.Property(r => r.CompletionTokens);
        builder.Property(r => r.CostUsd).HasPrecision(14, AiUsageRecord.CostDecimals);
        builder.Property(r => r.LatencyMs);
        builder.Property(r => r.Outcome).HasConversion<short>();
        builder.Property(r => r.CacheHit);
        builder.Property(r => r.CreatedAt);

        builder.HasOne<Shop>().WithMany().HasForeignKey(r => r.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        // Per-shop usage and cost in admin; also serves the shop_id FK.
        builder.HasIndex(r => new { r.ShopId, r.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_ai_usage_records_shop_id_created_at");

        // Platform-wide cost chart.
        builder.HasIndex(r => r.CreatedAt)
            .IsDescending()
            .HasDatabaseName("ix_ai_usage_records_created_at");

        // Invoice detail in admin; the model of an earlier extraction (InvoiceRepository.FindReusableExtractionAsync); FK.
        builder.HasIndex(r => r.InvoiceId)
            .HasDatabaseName("ix_ai_usage_records_invoice_id");
    }
}
