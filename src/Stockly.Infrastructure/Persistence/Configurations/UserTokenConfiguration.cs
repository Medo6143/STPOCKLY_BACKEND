using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Identity's <c>AspNetUserTokens</c> as <c>user_tokens</c>. Unused by Stockly today; part of Identity's user store.</summary>
internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
}
