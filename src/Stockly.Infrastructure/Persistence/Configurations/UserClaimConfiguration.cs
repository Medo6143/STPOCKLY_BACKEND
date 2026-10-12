using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Identity's <c>AspNetUserClaims</c> as <c>user_claims</c>. Unused by Stockly today; part of Identity's user store.</summary>
internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");
}
