using System.Globalization;
using Stockly.Application.Features.Products;
using Stockly.Contracts.Admin;
using Stockly.Domain.Platform;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;

namespace Stockly.Application.Features.Admin;

/// <summary>Admin DTO values: USD strings, plan names in the request language, plans.</summary>
public static class AdminMapping
{
    private const string EnglishCode = "en";

    private static readonly string UsdFormat = "F" + AiUsageRecord.CostDecimals.ToString(CultureInfo.InvariantCulture);

    /// <summary>A USD amount as the contract's <c>usd-string</c>: exactly 8 decimals.</summary>
    public static string Usd(decimal value) => value.ToString(UsdFormat, CultureInfo.InvariantCulture);

    /// <summary>The request's language (<c>Accept-Language</c>, default Arabic), from the UI culture the Api set.</summary>
    public static PreferredLanguage RequestLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == EnglishCode ? PreferredLanguage.En : PreferredLanguage.Ar;

    /// <summary>The plan name to show in <paramref name="language"/>.</summary>
    public static string PlanName(string nameAr, string nameEn, PreferredLanguage language) =>
        language == PreferredLanguage.En ? nameEn : nameAr;

    public static PlanDto ToDto(this Plan plan, PreferredLanguage language)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new PlanDto(
            plan.Id,
            plan.Code,
            PlanName(plan.NameAr, plan.NameEn, language),
            plan.DailyScanQuota,
            plan.MaxMembers,
            ProductMapping.Money(plan.MonthlyPrice),
            plan.IsActive);
    }

    /// <summary>The audited fields of a plan.</summary>
    public static object AuditState(this Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new
        {
            plan.Code,
            plan.NameAr,
            plan.NameEn,
            plan.DailyScanQuota,
            plan.MaxMembers,
            MonthlyPrice = ProductMapping.Money(plan.MonthlyPrice),
            plan.IsActive,
        };
    }
}
