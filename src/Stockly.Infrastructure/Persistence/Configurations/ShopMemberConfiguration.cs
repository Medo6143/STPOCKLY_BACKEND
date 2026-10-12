using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>
/// Not a tenant entity (ADR-0002, update 2026-10-12): read only through <c>ShopMembershipRepository</c>, which always
/// filters by user id. There is deliberately no <c>DbSet</c> property for it on the context.
/// </summary>
internal sealed class ShopMemberConfiguration : IEntityTypeConfiguration<ShopMember>
{
    public void Configure(EntityTypeBuilder<ShopMember> builder)
    {
        builder.ToTable("shop_members");
        builder.HasKey(m => new { m.ShopId, m.UserId });

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(m => m.ShopId);
        builder.Property(m => m.UserId);
        builder.Property(m => m.JoinedAt);
        builder.Property(m => m.Role).HasConversion<short>();

        builder.HasOne<Shop>().WithMany().HasForeignKey(m => m.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);

        // "Which shops can I use" at login; also serves the user_id foreign key.
        builder.HasIndex(m => m.UserId)
            .HasFilter("is_active")
            .HasDatabaseName("ix_shop_members_user_id_active");

        // One owner per shop.
        builder.HasIndex(m => m.ShopId)
            .IsUnique()
            .HasFilter($"role = {(short)ShopRole.Owner}")
            .HasDatabaseName("ux_shop_members_shop_id_owner");
    }
}
