using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Identity's <c>AspNetUserLogins</c> as <c>user_logins</c>. Unused by Stockly today; part of Identity's user store.</summary>
internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");
}
