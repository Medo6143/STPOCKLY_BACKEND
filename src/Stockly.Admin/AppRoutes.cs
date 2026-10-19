namespace Stockly.Admin;

/// <summary>Client-side page addresses used for navigation. The <c>@page</c> directives repeat them as literals.</summary>
public static class AppRoutes
{
    public const string Overview = "";
    public const string Login = "login";
    public const string Shops = "shops";
    public const string Plans = "plans";
    public const string AiUsage = "ai-usage";
    public const string FailedScans = "failed-scans";
    public const string AuditLog = "audit-log";
    public const string Payments = "payments";
    public const string ReturnUrlParameter = "returnUrl";

    public static string Shop(Guid id) => $"{Shops}/{id}";

    public static string FailedScan(Guid id) => $"{FailedScans}/{id}";

    public static string FailedScansOfShop(Guid shopId) => $"{FailedScans}?shopId={shopId}";

    public static string PaymentsOfShop(Guid shopId) => $"{Payments}?shopId={shopId}";

    public static string AuditLogOfTarget(string targetType, Guid targetId) =>
        $"{AuditLog}?targetType={Uri.EscapeDataString(targetType)}&targetId={targetId}";

    public static string LoginReturningTo(string relativeUrl) =>
        string.IsNullOrEmpty(relativeUrl) ? Login : $"{Login}?{ReturnUrlParameter}={Uri.EscapeDataString(relativeUrl)}";
}

/// <summary>The <c>targetType</c> values of the audit log (contract §11.1).</summary>
public static class AuditTargetTypes
{
    public const string Shop = "Shop";
    public const string Plan = "Plan";
    public const string Invoice = "Invoice";

    public static IReadOnlyList<string> All { get; } = [Shop, Plan, Invoice];
}
