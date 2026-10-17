using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class ShopSubscriptionConfiguration : IEntityTypeConfiguration<ShopSubscription>
{
    public const string TableName = "shop_subscriptions";

    private static readonly string ActiveFilter = $"status = {(short)SubscriptionStatus.Active}";

    public void Configure(EntityTypeBuilder<ShopSubscription> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(
            "ck_shop_subscriptions_daily_scan_quota_override",
            $"daily_scan_quota_override IS NULL OR daily_scan_quota_override BETWEEN 0 AND {ShopSubscription.MaxQuotaOverride}"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(s => s.ShopId);
        builder.Property(s => s.PlanId);
        builder.Property(s => s.StartsOn);
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.Status).HasConversion<short>();

        builder.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

        // One active subscription per shop; also serves the shop_id foreign key.
        builder.HasIndex(s => s.ShopId)
            .IsUnique()
            .HasFilter(ActiveFilter)
            .HasDatabaseName("ux_shop_subscriptions_shop_id_active");

        // Expiry job.
        builder.HasIndex(s => s.EndsOn)
            .HasFilter(ActiveFilter)
            .HasDatabaseName("ix_shop_subscriptions_ends_on_active");
    }
}
