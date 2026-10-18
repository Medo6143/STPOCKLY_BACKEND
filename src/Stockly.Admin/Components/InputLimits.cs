namespace Stockly.Admin.Components;

/// <summary>
/// Input limits of contract §11.3, mirrored for early feedback in the forms. The API validates again and stays the
/// authority; its <c>400 Validation.Failed</c> is shown as is.
/// </summary>
public static class InputLimits
{
    public const int ReasonMaxLength = 500;
    public const int SearchMaxLength = 100;
    public const string PlanCodePattern = "^[a-z0-9-]{2,32}$";
    public const int PlanNameMaxLength = 100;
    public const int MaxDailyScanQuota = 10_000;
    public const int MinPlanMembers = 1;
    public const int MaxPlanMembers = 1_000;

    /// <summary>Money request strings: up to 2 decimals, not negative (contract §1.2).</summary>
    public const string MoneyPattern = @"^\d{1,16}(\.\d{1,2})?$";

    /// <summary>AD14–AD16: <c>to - from</c> is at most 92 days.</summary>
    public const int MaxUsageRangeDays = 92;

    /// <summary>Default AI usage range: the last 30 days, today included.</summary>
    public const int DefaultUsageRangeDays = 30;

    public const int DefaultPageSize = 20;
}
