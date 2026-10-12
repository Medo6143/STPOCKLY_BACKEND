using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Configurations;

/// <summary>Plans are global (not tenant entities). The default plan is seeded from <see cref="DefaultPlan"/>.</summary>
internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(p => p.Code).IsRequired();
        builder.Property(p => p.NameAr).IsRequired();
        builder.Property(p => p.NameEn).IsRequired();
        builder.Property(p => p.MonthlyPrice).HasPrecision(18, 2);

        // Seed and lookup by code.
        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("ux_plans_code");

        builder.HasData(new
        {
            DefaultPlan.Id,
            Code = DefaultPlan.Code,
            NameAr = DefaultPlan.NameAr,
            NameEn = DefaultPlan.NameEn,
            DailyScanQuota = DefaultPlan.DailyScanQuota,
            MaxMembers = DefaultPlan.MaxMembers,
            MonthlyPrice = DefaultPlan.MonthlyPrice,
            IsActive = DefaultPlan.IsActive,
        });
    }
}
