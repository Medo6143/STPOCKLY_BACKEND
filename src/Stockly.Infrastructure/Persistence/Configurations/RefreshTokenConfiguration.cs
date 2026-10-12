using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Refresh tokens belong to a user, not a shop: no tenant filter (ADR-0002, update 2026-10-12).</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public const string TableName = "refresh_tokens";

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(t => t.UserId);
        builder.Property(t => t.TokenHash).IsRequired();
        builder.Property(t => t.FamilyId);
        builder.Property(t => t.DeviceId);
        builder.Property(t => t.ExpiresAt);
        builder.Property(t => t.CreatedAt);

        builder.Ignore(t => t.IsRevoked);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        // Refresh and logout look a presented token up by its hash.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        // Logout-all and chain revoke by user (erd.md §3.1); also serves the user_id foreign key.
        builder.HasIndex(t => t.UserId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_tokens_user_id_active");

        // Family revoke on reuse detection and logout.
        builder.HasIndex(t => t.FamilyId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_tokens_family_id_active");
    }
}
