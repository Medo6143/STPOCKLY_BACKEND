using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Plan payments (ADR-0015, contract §12). Indexes and the queries they serve in docs/erd.md §3.8.</summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string TableName = "payments";

    public const string ClientRequestIndex = "ux_payments_shop_id_client_request_id";

    public const string ProviderTransactionIndex = "ux_payments_provider_transaction_id";

    private const int CurrencyLength = 3;

    private static readonly string PendingFilter = $"status = {(short)PaymentStatus.Pending}";
    private static readonly string PaidFilter = $"status = {(short)PaymentStatus.Paid}";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_payments_amount", "amount > 0");
            table.HasCheckConstraint("ck_payments_paid_at", $"({PaidFilter}) = (paid_at IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(p => p.ShopId);
        builder.Property(p => p.PlanId);
        builder.Property(p => p.ClientRequestId);
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasMaxLength(CurrencyLength).IsFixedLength().IsRequired();
        builder.Property(p => p.Method).HasConversion<short>();
        builder.Property(p => p.Provider).HasConversion<short>();
        builder.Property(p => p.Status).HasConversion<short>();
        builder.Property(p => p.ProviderOrderId).HasMaxLength(Payment.ProviderIdMaxLength).IsRequired();
        builder.Property(p => p.ProviderTransactionId).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(Payment.CheckoutUrlMaxLength);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(Payment.ReferenceNumberMaxLength);
        builder.Property(p => p.FailureReason).HasMaxLength(Payment.FailureReasonMaxLength);
        builder.Property<DateTimeOffset>(ShadowProperties.UpdatedAt);

        builder.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);

        // B3 idempotency (ADR-0006): the repeat lookup; the insert of a concurrent repeat loses here.
        builder.HasIndex(p => new { p.ShopId, p.ClientRequestId }, ClientRequestIndex)
            .HasDatabaseName(ClientRequestIndex)
            .IsUnique();

        // Callback idempotency (contract §12.3): one payment per provider transaction.
        builder.HasIndex(p => p.ProviderTransactionId, ProviderTransactionIndex)
            .HasDatabaseName(ProviderTransactionIndex)
            .IsUnique()
            .HasFilter("provider_transaction_id IS NOT NULL");

        // Callback and reconcile: the payment of a provider order.
        builder.HasIndex(p => new { p.Provider, p.ProviderOrderId }, "ux_payments_provider_provider_order_id")
            .HasDatabaseName("ux_payments_provider_provider_order_id")
            .IsUnique();

        // B5 newest first; AD21 with shopId.
        builder.HasIndex(p => new { p.ShopId, p.CreatedAt }, "ix_payments_shop_id_created_at")
            .HasDatabaseName("ix_payments_shop_id_created_at")
            .IsDescending(false, true);

        // B2 newest pending payment; B3 open-payment check.
        builder.HasIndex(p => new { p.ShopId, p.CreatedAt }, "ix_payments_shop_id_created_at_pending")
            .HasDatabaseName("ix_payments_shop_id_created_at_pending")
            .IsDescending(false, true)
            .HasFilter(PendingFilter);

        // AD21 without filters, newest first.
        builder.HasIndex(p => p.CreatedAt, "ix_payments_created_at")
            .HasDatabaseName("ix_payments_created_at")
            .IsDescending(true);

        // AD21 with status.
        builder.HasIndex(p => new { p.Status, p.CreatedAt }, "ix_payments_status_created_at")
            .HasDatabaseName("ix_payments_status_created_at")
            .IsDescending(false, true);

        // Reconcile job: pending payments past their expiry.
        builder.HasIndex(p => p.ExpiresAt, "ix_payments_expires_at_pending")
            .HasDatabaseName("ix_payments_expires_at_pending")
            .HasFilter(PendingFilter);

        // AD22: paid payments by paid_at range.
        builder.HasIndex(p => p.PaidAt, "ix_payments_paid_at_paid")
            .HasDatabaseName("ix_payments_paid_at_paid")
            .HasFilter(PaidFilter);
    }
}
