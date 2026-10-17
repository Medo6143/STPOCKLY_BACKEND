using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

/// <summary>A subscription plan. Global (not a tenant entity): every shop's subscription points to one.</summary>
public sealed class Plan : Entity
{
    private Plan(string code, string nameAr, string nameEn, int dailyScanQuota, int maxMembers, decimal monthlyPrice, bool isActive)
    {
        Code = code;
        NameAr = nameAr;
        NameEn = nameEn;
        DailyScanQuota = dailyScanQuota;
        MaxMembers = maxMembers;
        MonthlyPrice = monthlyPrice;
        IsActive = isActive;
    }

    /// <summary>Stable lookup key, lower-case (e.g. <c>standard</c>).</summary>
    public string Code { get; }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    /// <summary>AI invoice scans per shop per Cairo day.</summary>
    public int DailyScanQuota { get; private set; }

    /// <summary>Owner included.</summary>
    public int MaxMembers { get; private set; }

    /// <summary>EGP per month.</summary>
    public decimal MonthlyPrice { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Plan> Create(
        string code, string nameAr, string nameEn, int dailyScanQuota, int maxMembers, decimal monthlyPrice, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return PlanErrors.CodeRequired;
        }

        var error = Validate(nameAr, nameEn, dailyScanQuota, maxMembers, monthlyPrice);
        if (error is not null)
        {
            return error;
        }

        return new Plan(
            code.Trim().ToLowerInvariant(), nameAr.Trim(), nameEn.Trim(), dailyScanQuota, maxMembers, monthlyPrice, isActive);
    }

    /// <summary>Changes everything but the <see cref="Code"/>, which is immutable (contract AD13). On failure nothing changes.</summary>
    public Result Update(string nameAr, string nameEn, int dailyScanQuota, int maxMembers, decimal monthlyPrice, bool isActive)
    {
        var error = Validate(nameAr, nameEn, dailyScanQuota, maxMembers, monthlyPrice);
        if (error is not null)
        {
            return error;
        }

        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        DailyScanQuota = dailyScanQuota;
        MaxMembers = maxMembers;
        MonthlyPrice = monthlyPrice;
        IsActive = isActive;
        return Result.Success();
    }

    private static Error? Validate(string nameAr, string nameEn, int dailyScanQuota, int maxMembers, decimal monthlyPrice)
    {
        if (string.IsNullOrWhiteSpace(nameAr) || string.IsNullOrWhiteSpace(nameEn))
        {
            return PlanErrors.NameRequired;
        }

        if (dailyScanQuota < 0)
        {
            return PlanErrors.NegativeQuota;
        }

        if (maxMembers < 1)
        {
            return PlanErrors.NoMembers;
        }

        return Precision.ValidateMoney(monthlyPrice, nameof(monthlyPrice));
    }
}
