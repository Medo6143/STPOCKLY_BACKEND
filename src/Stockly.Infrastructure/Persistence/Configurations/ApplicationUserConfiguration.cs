using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Remaps Identity's <c>AspNetUsers</c> to <c>users</c> (docs/erd.md §3.1). Runs after Identity's own model.</summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(u => u.FullName).IsRequired();
        builder.Property(u => u.PhoneNumber).HasColumnName("phone");
        builder.Property(u => u.PreferredLanguage).HasConversion<short>();
        builder.Property(u => u.PlatformRole).HasConversion<short>();

        // Login, and case-insensitive unique email (Identity normalizes to upper case).
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");

        // Identity's own user-name index; the user name is the email.
        builder.HasIndex(u => u.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_user_name");
    }
}
