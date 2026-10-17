using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Global and append-only (ADR-0005): no tenant filter. Indexes serve AD20's filters (docs/erd.md §3.6).</summary>
internal sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("admin_audit_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // Every property is get-only (append-only row); map them explicitly.
        builder.Property(l => l.AdminUserId);
        builder.Property(l => l.Action).IsRequired().HasMaxLength(AdminAuditLog.NameMaxLength);
        builder.Property(l => l.TargetType).IsRequired().HasMaxLength(AdminAuditLog.NameMaxLength);
        builder.Property(l => l.TargetId);
        builder.Property(l => l.Reason);
        builder.Property(l => l.Before).HasColumnType("jsonb");
        builder.Property(l => l.After).HasColumnType("jsonb");
        builder.Property(l => l.CreatedAt);

        // Named explicitly: this configuration runs before the one that renames AspNetUsers to users.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.AdminUserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_admin_audit_logs_users_admin_user_id");

        // AD20 by target ("history of this shop"); with only targetType, its prefix.
        builder.HasIndex(l => new { l.TargetType, l.TargetId, l.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_admin_audit_logs_target_type_target_id_created_at");

        // AD20 by admin ("what did this admin do"); also serves the admin_user_id foreign key.
        builder.HasIndex(l => new { l.AdminUserId, l.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_admin_audit_logs_admin_user_id_created_at");

        // AD20 without filters, newest first.
        builder.HasIndex(l => l.CreatedAt)
            .IsDescending()
            .HasDatabaseName("ix_admin_audit_logs_created_at");
    }
}
