namespace Stockly.Domain.Shops;

/// <summary>
/// The plan every new shop is subscribed to at registration, seeded by the <c>Identity</c> migration. The values are
/// product-owner assumptions (docs/features/auth.md, "Assumptions"): changing one is a one-line change here plus a
/// new migration. The quota is low because AI scans cost money.
/// </summary>
public static class DefaultPlan
{
    public const string Code = "standard";
    public const string NameAr = "الأساسية";
    public const string NameEn = "Standard";
    public const int DailyScanQuota = 30;
    public const int MaxMembers = 3;
    public const decimal MonthlyPrice = 0.00m;
    public const bool IsActive = true;

    /// <summary>Fixed so the seed row has the same id in every database.</summary>
    public static readonly Guid Id = new("0192f0a0-0000-7000-8000-000000000001");
}
